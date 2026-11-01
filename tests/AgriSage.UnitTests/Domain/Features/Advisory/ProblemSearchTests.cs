using AgriSage.Domain.Features.Advisory;

namespace AgriSage.UnitTests.Domain.Features.Advisory;

public class ProblemSearchTests
{
    private static readonly ProblemSearch.Candidate Blast = new(
        new Guid("00000000-0000-0000-0000-000000000001"), "Bệnh đạo ôn lá", "cháy lá, đạo ôn cổ bông", "Vết hình thoi, tâm xám trắng, viền nâu đỏ");
    private static readonly ProblemSearch.Candidate Sheath = new(
        new Guid("00000000-0000-0000-0000-000000000002"), "Bệnh khô vằn", "vằn da hổ", "Vết bệnh trên bẹ lá gần mặt nước");
    private static readonly ProblemSearch.Candidate Nitrogen = new(
        new Guid("00000000-0000-0000-0000-000000000003"), "Thiếu đạm", "vàng lá, lúa còi", "Lá già vàng đều từ chóp xuống");
    private static readonly ProblemSearch.Candidate[] All = [Blast, Sheath, Nitrogen];

    [Theory]
    [InlineData("Bệnh Đạo Ôn", "benh dao on")]
    [InlineData("  đạo   ôn ", "dao on")]
    [InlineData("Vàng lá!", "vang la")]
    [InlineData(null, "")]
    public void Normalize_ignores_accents_case_and_punctuation(string? input, string expected)
    {
        Assert.Equal(expected, ProblemSearch.Normalize(input));
    }

    [Theory]
    [InlineData("dao on")]
    [InlineData("đạo ôn")]
    [InlineData("DẠO ON")]
    public void Typing_without_accents_finds_the_problem(string query)
    {
        var hit = Assert.Single(ProblemSearch.Rank(All, query));

        Assert.Equal(Blast.Id, hit.Candidate.Id);
        Assert.Equal(ProblemSearch.Match.Name, hit.MatchedBy);
    }

    [Fact]
    public void A_keyword_finds_a_problem_whose_name_does_not_contain_it()
    {
        var hit = Assert.Single(ProblemSearch.Rank(All, "chay la"));

        Assert.Equal(Blast.Id, hit.Candidate.Id);
        Assert.Equal(ProblemSearch.Match.Keyword, hit.MatchedBy);
    }

    [Fact]
    public void A_symptom_finds_a_problem_when_nothing_else_does()
    {
        var hit = Assert.Single(ProblemSearch.Rank(All, "be la gan mat nuoc"));

        Assert.Equal(Sheath.Id, hit.Candidate.Id);
        Assert.Equal(ProblemSearch.Match.Symptom, hit.MatchedBy);
    }

    [Fact]
    public void Name_matches_come_before_keyword_matches_before_symptom_matches()
    {
        var byName = new ProblemSearch.Candidate(Guid.NewGuid(), "Vàng lá chân", null, null);
        var byKeyword = new ProblemSearch.Candidate(Guid.NewGuid(), "Thiếu đạm", "vàng lá", null);
        var bySymptom = new ProblemSearch.Candidate(Guid.NewGuid(), "Thiếu kali", null, "cháy vàng lá già");

        var ids = ProblemSearch.Rank([bySymptom, byKeyword, byName], "vang la").Select(h => h.Candidate.Id).ToList();

        Assert.Equal([byName.Id, byKeyword.Id, bySymptom.Id], ids);
    }

    [Fact]
    public void Every_word_of_the_query_must_match()
    {
        Assert.Empty(ProblemSearch.Rank(All, "dao on rep"));
    }

    [Fact]
    public void An_empty_query_returns_every_problem_by_name()
    {
        var names = ProblemSearch.Rank(All, "  ").Select(h => h.Candidate.Name).ToList();

        Assert.Equal(["Bệnh đạo ôn lá", "Bệnh khô vằn", "Thiếu đạm"], names);
        Assert.All(ProblemSearch.Rank(All, null), hit => Assert.Equal(ProblemSearch.Match.None, hit.MatchedBy));
    }
}
