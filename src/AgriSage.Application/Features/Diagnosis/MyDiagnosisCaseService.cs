using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Files;
using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = AgriSage.Application.Common.Exceptions.ValidationException;

namespace AgriSage.Application.Features.Diagnosis;

// The Farmer's own diagnosis cases (/api/me/diagnosis-cases). Identity comes from the JWT; another Farmer's case is 404.
//
// Create (AI_DIAGNOSIS.md D4): upload the photo, commit the case (SUBMITTED), call the AI with no transaction open, then
// record the inference (DiagnosisAiRunner). The answer already carries the AI result (2.0, no Human Review): a disease
// and a certainty, never a probability (D8). A failed analysis is retried by the Farmer (D14).
public sealed class MyDiagnosisCaseService(
    IAgriSageDbContext context,
    CurrentFarmer currentFarmer,
    IFileStorageService storage,
    IPrivateFileStore privateFiles,
    IDateTimeProvider clock,
    DiagnosisAiRunner runner,
    DiagnosisResultBuilder results,
    DiagnosisImageUrls imageUrls,
    AuditTrail audit,
    ILogger<MyDiagnosisCaseService> logger) : IMyDiagnosisCaseService
{
    private const int MaxNoteLength = 1000;
    private const int MaxFileNameLength = 255;

    public async Task<MyDiagnosisCaseResponse> CreateAsync(
        Stream image, string? fileName, string? note, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        var cleanNote = Texts.Clean(note);
        if (cleanNote is { Length: > MaxNoteLength })
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["note"] = [$"The note must not be longer than {MaxNoteLength} characters."]
            });
        }

        var (stored, bytes, type) = await ImageUploader.UploadWithContentAsync(
            storage, clock, StorageArea.DiagnosisImages, ImageRules.MaxDiagnosisBytes, image, cancellationToken);

        DiagnosisCase diagnosisCase;
        try
        {
            var now = clock.UtcNow;
            var number = await DocumentNumbers.NextAsync(
                context.DiagnosisCases.IgnoreQueryFilters().AsNoTracking().Select(c => c.CaseNumber),
                DocumentNumbers.DiagnosisCase, BusinessCalendar.Today(now), cancellationToken);

            diagnosisCase = new DiagnosisCase(farmer.FarmerProfileId, number, now, cleanNote);
            diagnosisCase.AddImage(
                stored.StorageKey, stored.Url, now, isPrimary: true, CleanFileName(fileName), type.ContentType, stored.SizeBytes);
            context.DiagnosisCases.Add(diagnosisCase);
            audit.Record("DIAGNOSIS_CASE_CREATED", "DIAGNOSIS_CASE", diagnosisCase.Id, storeId, newValues: new { diagnosisCase.CaseNumber });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await TryDeleteAsync(stored.StorageKey);
            throw;
        }

        await runner.RunAsync(diagnosisCase.Id, bytes, type.ContentType, $"photo{type.Extension}", cancellationToken);

        return await GetAsync(diagnosisCase.Id, cancellationToken);
    }

    public async Task<PagedResult<MyDiagnosisCaseListItem>> ListAsync(
        MyDiagnosisCaseListRequest request, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var query = context.DiagnosisCases.AsNoTracking().Where(c => c.FarmerProfileId == farmer.FarmerProfileId);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EnumText.TryParse<DiagnosisCaseStatus>(request.Status, out var status))
            {
                throw new ValidationException(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] });
            }

            query = query.Where(c => c.Status == status);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(c => c.SubmittedAt).ThenBy(c => c.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(c => new
            {
                c.Id,
                c.CaseNumber,
                c.Status,
                c.SubmittedAt,
                c.CompletedAt,
                FinalCode = c.FinalDisease != null ? c.FinalDisease.Code : null,
                FinalName = c.FinalDisease != null ? c.FinalDisease.Name : null
            })
            .ToListAsync(cancellationToken);

        var latest = await results.LatestAsync(rows.Select(r => r.Id).ToList(), cancellationToken);
        var diseases = (await context.Diseases.AsNoTracking().Select(d => new { d.Id, d.Code, d.Name }).ToListAsync(cancellationToken))
            .ToDictionary(d => d.Id);

        var items = rows.Select(r =>
        {
            var inference = latest.GetValueOrDefault(r.Id);
            var certainty = DiagnosisResultPolicy.CertaintyOf(r.Status, inference?.PassedPolicy);
            string? code = null;
            string? name = null;

            if (certainty == DiagnosisCertainty.Verified)
            {
                code = r.FinalCode;
                name = r.FinalName;
            }
            else if (certainty is not null && inference?.PredictedDiseaseId is { } diseaseId
                && diseases.TryGetValue(diseaseId, out var disease))
            {
                code = disease.Code;
                name = disease.Name;
            }

            return new MyDiagnosisCaseListItem(
                r.Id, r.CaseNumber, EnumText.Format(r.Status), r.SubmittedAt, r.CompletedAt, code, name,
                code is null || certainty is null ? null : EnumText.Format(certainty.Value));
        }).ToList();

        return new PagedResult<MyDiagnosisCaseListItem>(items, request.Page, request.PageSize, total);
    }

    public async Task<MyDiagnosisCaseResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var diagnosisCase = await context.DiagnosisCases.AsNoTracking()
                .Include(c => c.Images)
                .Include(c => c.FinalDisease)
                .FirstOrDefaultAsync(c => c.Id == id && c.FarmerProfileId == farmer.FarmerProfileId, cancellationToken)
            ?? throw new NotFoundException("Diagnosis case", id);

        var images = await imageUrls.ForAsync(diagnosisCase.Images, cancellationToken);
        var result = await results.ForCaseAsync(id, diagnosisCase.Status, diagnosisCase.FinalDisease, cancellationToken);
        DiagnosisInconclusiveResponse? inconclusive = null;

        // Decided INCONCLUSIVE by a reviewer before 2.0: the comment and a retake hint stay readable (D15).
        if (diagnosisCase.Status == DiagnosisCaseStatus.Inconclusive)
        {
            var comment = await context.AgentReviews.AsNoTracking()
                .Where(r => r.DiagnosisCaseId == id && r.IsCurrent)
                .Select(r => r.Comment)
                .FirstOrDefaultAsync(cancellationToken);
            inconclusive = new DiagnosisInconclusiveResponse(comment, "RETAKE_PHOTO");
        }

        return new MyDiagnosisCaseResponse(
            diagnosisCase.Id,
            diagnosisCase.CaseNumber,
            EnumText.Format(diagnosisCase.Status),
            diagnosisCase.SubmittedAt,
            diagnosisCase.CompletedAt,
            diagnosisCase.FarmerNote,
            images.FirstOrDefault(),
            result,
            inconclusive,
            DiagnosisResultPolicy.CanRetry(diagnosisCase.Status, diagnosisCase.SubmittedAt, clock.UtcNow));
    }

    public async Task<MyDiagnosisCaseResponse> CancelAsync(Guid id, CancelDiagnosisRequest request, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var diagnosisCase = await context.DiagnosisCases
                .FirstOrDefaultAsync(c => c.Id == id && c.FarmerProfileId == farmer.FarmerProfileId, cancellationToken)
            ?? throw new NotFoundException("Diagnosis case", id);

        var before = EnumText.Format(diagnosisCase.Status);
        diagnosisCase.Cancel();
        audit.Record(
            "DIAGNOSIS_CASE_CANCELLED", "DIAGNOSIS_CASE", diagnosisCase.Id, storeId,
            new { Status = before }, new { Status = EnumText.Format(diagnosisCase.Status) }, Texts.Clean(request.Reason));
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<MyDiagnosisCaseResponse> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var found = await context.DiagnosisCases.AsNoTracking()
                .Where(c => c.Id == id && c.FarmerProfileId == farmer.FarmerProfileId)
                .Select(c => new
                {
                    c.Status,
                    c.SubmittedAt,
                    Photo = c.Images.Where(i => i.DeletedAt == null)
                        .OrderByDescending(i => i.IsPrimary)
                        .Select(i => new { i.StorageKey, i.MimeType })
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Diagnosis case", id);

        if (!DiagnosisResultPolicy.CanRetry(found.Status, found.SubmittedAt, clock.UtcNow))
        {
            throw new BusinessRuleException("Only a case whose analysis failed can be analysed again.");
        }

        var photo = found.Photo ?? throw new BusinessRuleException("The case has no photo to analyse.");
        var bytes = await privateFiles.ReadAsync(photo.StorageKey, StorageArea.DiagnosisImages, cancellationToken);
        var contentType = photo.MimeType ?? ImageRules.ContentTypeOf(photo.StorageKey) ?? "image/jpeg";

        // An unreachable AI Service throws (503) and records nothing, so retrying while it is down piles up no FAILED rows.
        await runner.RunAsync(id, bytes, contentType, "photo", cancellationToken, failWhenUnavailable: true);

        var after = await context.DiagnosisCases.AsNoTracking()
            .Where(c => c.Id == id).Select(c => c.Status).FirstAsync(cancellationToken);
        audit.Record(
            "DIAGNOSIS_AI_RETRIED", "DIAGNOSIS_CASE", id, storeId,
            new { Status = EnumText.Format(found.Status) }, new { Status = EnumText.Format(after) });
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    // A display name only: no path, no control characters, bounded.
    private static string? CleanFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = new string(Path.GetFileName(fileName.Trim()).Where(c => !char.IsControl(c)).ToArray());

        return name.Length == 0 ? null : name[..Math.Min(name.Length, MaxFileNameLength)];
    }

    private async Task TryDeleteAsync(string storageKey)
    {
        try
        {
            await storage.DeleteAsync(storageKey, StorageArea.DiagnosisImages, CancellationToken.None);
        }
        catch (Exception exception) when (exception is StorageUnavailableException or HttpRequestException)
        {
            logger.LogWarning("An uploaded diagnosis photo could not be removed after the case failed to save.");
        }
    }
}
