using System.Text.RegularExpressions;

namespace AgriSage.UnitTests.Architecture;

// Bulk EF APIs run SQL directly and bypass the SaveChanges interceptors: no timestamps, soft delete, version
// increment or concurrency check (coding rule #64, database design §35.15). Scans production source code.
public partial class ForbiddenPersistenceApiTests
{
    // Files allowed to call ExecuteUpdate/ExecuteUpdateAsync after an explicit team review.
    // Each entry: repository-relative path with '/' separators, plus a comment explaining why it is safe.
    private static readonly HashSet<string> ReviewedExecuteUpdateFiles = [];

    [Fact]
    public void Production_code_never_uses_ExecuteDelete()
    {
        var usages = FindUsages(ExecuteDeletePattern());

        Assert.True(usages.Count == 0, "ExecuteDelete bypasses soft delete:\n" + string.Join('\n', usages));
    }

    [Fact]
    public void Production_code_uses_ExecuteUpdate_only_in_reviewed_files()
    {
        var usages = FindUsages(ExecuteUpdatePattern())
            .Where(usage => !ReviewedExecuteUpdateFiles.Contains(usage.Split(':')[0]))
            .ToList();

        Assert.True(usages.Count == 0, "ExecuteUpdate bypasses audit/version interceptors:\n" + string.Join('\n', usages));
    }

    [Theory]
    [InlineData("await db.Carts.Where(c => c.IsAbandoned).ExecuteDeleteAsync(ct);", true)]
    [InlineData("db.Carts.ExecuteDelete();", true)]
    [InlineData("// db.Carts.ExecuteDeleteAsync(ct) is forbidden", false)]
    [InlineData("/// <see cref=\"ExecuteDelete\"/>", false)]
    [InlineData("var executeDeleted = true;", false)]
    public void Scanner_detects_calls_and_ignores_comments(string line, bool expected)
    {
        Assert.Equal(expected, IsCodeUsage(line, ExecuteDeletePattern()));
    }

    private static List<string> FindUsages(Regex pattern)
    {
        var root = FindRepositoryRoot();
        var separator = Path.DirectorySeparatorChar;

        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}") && !file.Contains($"{separator}bin{separator}"))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(candidate => IsCodeUsage(candidate.line, pattern))
            .Select(candidate =>
                $"{Path.GetRelativePath(root, candidate.file).Replace('\\', '/')}:{candidate.number}: {candidate.line.Trim()}")
            .ToList();
    }

    private static bool IsCodeUsage(string line, Regex pattern)
    {
        var trimmed = line.TrimStart();

        return !trimmed.StartsWith("//", StringComparison.Ordinal)
            && !trimmed.StartsWith('*')
            && pattern.IsMatch(line);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgriSage.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate AgriSage.sln above the test output directory.");
    }

    [GeneratedRegex(@"\bExecuteDelete(Async)?\b")]
    private static partial Regex ExecuteDeletePattern();

    [GeneratedRegex(@"\bExecuteUpdate(Async)?\b")]
    private static partial Regex ExecuteUpdatePattern();
}
