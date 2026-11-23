namespace AgriSage.Application.Features.Reports;

// GET /api/reports/advisory-diagnosis (REPORTS.md "Advisory and diagnosis — 2026-11-23").
public sealed record AdvisoryDiagnosisReportRequest : ReportPeriodRequest;

public sealed record AdvisoryDiagnosisReportResponse(
    DateOnly FromDate,
    DateOnly ToDate,
    int AttributionDays,
    AdvisoryReportTotals Advisory,
    IReadOnlyList<AdvisoryTopProblem> TopProblems,
    DiagnosisReportTotals Diagnosis);

public sealed record AdvisoryReportTotals(
    int SessionCount,
    int FarmerCount,
    int TextSessionCount,
    int DiagnosisSessionCount,
    int SessionsWithProducts,
    decimal EstimatedValue,
    int ConvertedSessions,
    decimal? ConversionPercent,
    decimal ConvertedOrderValue);

public sealed record AdvisoryTopProblem(Guid DiseaseId, string Code, string Name, string CropType, int SessionCount);

public sealed record DiagnosisReportTotals(int CaseCount, IReadOnlyList<DiagnosisCropRow> ByCrop);

public sealed record DiagnosisCropRow(
    string CropType,
    string CropName,
    int CaseCount,
    int ConfidentCount,
    int UncertainCount,
    int VerifiedCount,
    int NoResultCount);

public interface IAdvisoryDiagnosisReportService
{
    Task<AdvisoryDiagnosisReportResponse> GetAsync(AdvisoryDiagnosisReportRequest request, CancellationToken token);
}

public sealed class AdvisoryDiagnosisReportRequestValidator : ReportPeriodValidator<AdvisoryDiagnosisReportRequest>;
