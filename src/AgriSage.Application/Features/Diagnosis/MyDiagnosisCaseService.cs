using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Files;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = AgriSage.Application.Common.Exceptions.ValidationException;

namespace AgriSage.Application.Features.Diagnosis;

// The Farmer's own diagnosis cases (/api/me/diagnosis-cases). Identity comes from the JWT; another Farmer's case is 404.
//
// Create (AI_DIAGNOSIS.md D4): upload the photo, commit the case (SUBMITTED), call the AI with no transaction open, then
// record the inference (DiagnosisAiRunner). The Farmer only ever sees a verified result (D8).
public sealed class MyDiagnosisCaseService(
    IAgriSageDbContext context,
    CurrentFarmer currentFarmer,
    IFileStorageService storage,
    IDateTimeProvider clock,
    DiagnosisAiRunner runner,
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
                // The disease is shown only once a reviewer verified it.
                Code = c.Status == DiagnosisCaseStatus.Verified && c.FinalDisease != null ? c.FinalDisease.Code : null,
                Name = c.Status == DiagnosisCaseStatus.Verified && c.FinalDisease != null ? c.FinalDisease.Name : null
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new MyDiagnosisCaseListItem(
                r.Id, r.CaseNumber, EnumText.Format(r.Status), r.SubmittedAt, r.CompletedAt, r.Code, r.Name))
            .ToList();

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
        DiagnosisResultResponse? result = null;
        DiagnosisInconclusiveResponse? inconclusive = null;

        if (diagnosisCase.Status is DiagnosisCaseStatus.Verified or DiagnosisCaseStatus.Inconclusive)
        {
            var review = await context.AgentReviews.AsNoTracking()
                .Where(r => r.DiagnosisCaseId == id && r.IsCurrent)
                .FirstOrDefaultAsync(cancellationToken);

            if (diagnosisCase.Status == DiagnosisCaseStatus.Verified && review is not null && diagnosisCase.FinalDisease is { } disease)
            {
                result = await BuildResultAsync(id, review, disease, cancellationToken);
            }
            else if (diagnosisCase.Status == DiagnosisCaseStatus.Inconclusive)
            {
                inconclusive = new DiagnosisInconclusiveResponse(review?.Comment, "RETAKE_PHOTO");
            }
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
            inconclusive);
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

    private async Task<DiagnosisResultResponse> BuildResultAsync(
        Guid caseId, AgentReview review, Disease disease, CancellationToken cancellationToken)
    {
        var items = await context.RecommendationItems.AsNoTracking()
            .Include(r => r.DiseaseTreatment)
            .Include(r => r.StoreProduct).ThenInclude(p => p!.Product)
            .Where(r => r.DiagnosisCaseId == caseId && r.AgentReviewId == review.Id && r.IsActive)
            .OrderBy(r => r.RankOrder).ThenBy(r => r.ApprovedAt)
            .ToListAsync(cancellationToken);

        var treatments = items
            .Where(r => r is { RecommendationType: RecommendationType.Treatment, DiseaseTreatment: { IsActive: true } })
            .Select(r => new TreatmentResponse(
                r.DiseaseTreatment!.Id,
                EnumText.Format(r.DiseaseTreatment.TreatmentType),
                r.DiseaseTreatment.Title,
                r.DiseaseTreatment.Instructions,
                r.DiseaseTreatment.Precautions,
                r.RankOrder,
                r.Reason))
            .ToList();

        // Only what can still be bought today: the catalog may have changed since the review.
        var products = items
            .Where(r => r is { RecommendationType: RecommendationType.Product, StoreProduct: { IsActive: true, IsSellable: true } })
            .Select(r => new RecommendedProductResponse(
                r.StoreProduct!.Id,
                r.StoreProduct.ProductId,
                r.StoreProduct.Product.Name,
                r.StoreProduct.StoreSku ?? r.StoreProduct.Product.Sku,
                r.StoreProduct.Product.ImageUrl,
                r.Reason,
                r.RankOrder))
            .ToList();

        return new DiagnosisResultResponse(
            new DiseaseSummary(disease.Id, disease.Code, disease.Name, disease.IsHealthyClass, disease.Symptoms, disease.Prevention),
            review.Comment,
            treatments,
            products);
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
