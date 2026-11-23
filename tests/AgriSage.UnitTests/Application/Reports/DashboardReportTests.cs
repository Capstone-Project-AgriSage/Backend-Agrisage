using AgriSage.Application.Features.Reports;
using AgriSage.Domain.Features.Diagnosis.Enums;

namespace AgriSage.UnitTests.Application.Reports;

// The dashboard reports of 2026-11-23 (REPORTS.md "Advisory and diagnosis", "My sales"): the per-crop certainty tally and
// the period rules. The SQL parts are covered against PostgreSQL in AdvisoryDiagnosisReportDatabaseTests.
public class DashboardReportTests
{
    private static DiagnosisCropTally.Group G(string crop, DiagnosisCaseStatus status, bool? passed, int count) =>
        new(crop, status, passed, count);

    [Fact]
    public void Each_case_counts_under_the_certainty_the_farmer_reads()
    {
        var rows = DiagnosisCropTally.ByCrop(
        [
            G("RICE", DiagnosisCaseStatus.AiCompleted, true, 4),
            G("RICE", DiagnosisCaseStatus.AiCompleted, false, 3),
            G("RICE", DiagnosisCaseStatus.UnderReview, true, 1), // an old case left in review: still the AI result
            G("RICE", DiagnosisCaseStatus.Verified, true, 2), // confirmed by the store before 2026-10-21
            G("RICE", DiagnosisCaseStatus.AiCompleted, null, 1), // no SUCCESS inference: no result to show
            G("RICE", DiagnosisCaseStatus.Failed, null, 5),
            G("RICE", DiagnosisCaseStatus.Submitted, null, 1),
            G("RICE", DiagnosisCaseStatus.Inconclusive, false, 1),
            G("RICE", DiagnosisCaseStatus.Cancelled, null, 1),
        ]);

        var rice = Assert.Single(rows);
        Assert.Equal(("RICE", "Lúa"), (rice.CropType, rice.CropName));
        Assert.Equal(19, rice.CaseCount);
        Assert.Equal(5, rice.ConfidentCount);
        Assert.Equal(3, rice.UncertainCount);
        Assert.Equal(2, rice.VerifiedCount);
        Assert.Equal(9, rice.NoResultCount);
        Assert.Equal(rice.CaseCount, rice.ConfidentCount + rice.UncertainCount + rice.VerifiedCount + rice.NoResultCount);
    }

    [Fact]
    public void Rows_follow_the_crop_catalog_and_an_unknown_crop_keeps_its_code_last()
    {
        var rows = DiagnosisCropTally.ByCrop(
        [
            G("PEPPER", DiagnosisCaseStatus.Failed, null, 1),
            G("MANGO", DiagnosisCaseStatus.AiCompleted, true, 1),
            G("RICE", DiagnosisCaseStatus.AiCompleted, false, 2),
            G("DURIAN", DiagnosisCaseStatus.AiCompleted, true, 1),
        ]);

        Assert.Equal(new[] { "RICE", "DURIAN", "MANGO", "PEPPER" }, rows.Select(r => r.CropType));
        Assert.Equal(new[] { "Lúa", "Sầu riêng", "Xoài", "PEPPER" }, rows.Select(r => r.CropName));
        Assert.Empty(DiagnosisCropTally.ByCrop([]));
    }

    [Fact]
    public void Both_reports_take_a_required_period_of_at_most_366_days()
    {
        var advisory = new AdvisoryDiagnosisReportRequestValidator();
        var valid = new AdvisoryDiagnosisReportRequest { FromDate = new(2026, 11, 1), ToDate = new(2026, 11, 30) };
        Assert.True(advisory.Validate(valid).IsValid);
        Assert.False(advisory.Validate(valid with { FromDate = null }).IsValid);
        Assert.False(advisory.Validate(valid with { ToDate = new(2026, 10, 31) }).IsValid);
        Assert.False(advisory.Validate(valid with { ToDate = new(2027, 11, 2) }).IsValid);

        var mine = new MySalesReportRequestValidator();
        var today = new MySalesReportRequest { FromDate = new(2026, 11, 23), ToDate = new(2026, 11, 23) };
        Assert.True(mine.Validate(today).IsValid);
        Assert.False(mine.Validate(today with { ToDate = null }).IsValid);
        Assert.False(mine.Validate(today with { FromDate = DateOnly.MinValue, ToDate = DateOnly.MinValue }).IsValid);
    }
}
