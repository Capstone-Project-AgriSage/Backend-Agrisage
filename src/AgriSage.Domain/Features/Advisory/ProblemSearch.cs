using System.Globalization;
using System.Text;

namespace AgriSage.Domain.Features.Advisory;

// Plain text matching of what a Farmer types against the problems of the store (api-flows/ADVISORY.md V3). Accents and
// case are ignored ("dao on" finds "Bệnh đạo ôn lá"); no model is involved.
public static class ProblemSearch
{
    public enum Match
    {
        None,
        Name,
        Keyword,
        Symptom
    }

    public sealed record Candidate(Guid Id, string Name, string? Keywords, string? Symptoms);

    public sealed record Hit(Candidate Candidate, Match MatchedBy);

    // Lower case, without accents; the Vietnamese letter đ becomes d; everything that is not a letter or digit is a space.
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // Every word of the query must appear in the name, the keywords or the symptoms. Order: all words in the name, then
    // in the keywords, then in the symptoms; ties by name. An empty query keeps every candidate in name order.
    public static IReadOnlyList<Hit> Rank(IEnumerable<Candidate> candidates, string? query)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var hits = candidates.Select(candidate => new Hit(candidate, words.Length == 0 ? Match.None : Classify(candidate, words)));

        return hits
            .Where(hit => words.Length == 0 || hit.MatchedBy != Match.None)
            .OrderBy(hit => hit.MatchedBy == Match.None ? 0 : (int)hit.MatchedBy)
            .ThenBy(hit => Normalize(hit.Candidate.Name), StringComparer.Ordinal)
            .ThenBy(hit => hit.Candidate.Id)
            .ToList();
    }

    private static Match Classify(Candidate candidate, string[] words)
    {
        var name = Normalize(candidate.Name);
        var keywords = Normalize(candidate.Keywords);
        var symptoms = Normalize(candidate.Symptoms);

        if (words.All(name.Contains))
        {
            return Match.Name;
        }

        if (words.All(word => name.Contains(word) || keywords.Contains(word)))
        {
            return Match.Keyword;
        }

        return words.All(word => name.Contains(word) || keywords.Contains(word) || symptoms.Contains(word))
            ? Match.Symptom
            : Match.None;
    }
}
