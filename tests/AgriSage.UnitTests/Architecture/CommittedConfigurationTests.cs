using System.Text.Json;

namespace AgriSage.UnitTests.Architecture;

// Committed appsettings files hold only non-secret values: the database password and JWT signing key come
// from User Secrets / environment variables (README "Configuration & secrets").
public class CommittedConfigurationTests
{
    public static TheoryData<string> AppSettingsFiles => new(
        Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(file => Path.GetRelativePath(FindRepositoryRoot(), file)));

    [Theory]
    [MemberData(nameof(AppSettingsFiles))]
    public void Appsettings_contain_no_secret_values(string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath)));
        var root = document.RootElement;

        if (root.TryGetProperty("Database", out var database))
        {
            Assert.False(database.TryGetProperty("Password", out _), $"{relativePath} must not contain Database:Password.");
        }

        Assert.False(root.TryGetProperty("ConnectionStrings", out _), $"{relativePath} must not contain a connection string.");

        if (root.TryGetProperty("AdminBootstrap", out var admin))
        {
            Assert.False(admin.TryGetProperty("Password", out _), $"{relativePath} must not contain AdminBootstrap:Password.");
        }

        if (root.TryGetProperty("Jwt", out var jwt) && jwt.TryGetProperty("SigningKey", out var signingKey))
        {
            Assert.Equal(string.Empty, signingKey.GetString());
        }
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
}
