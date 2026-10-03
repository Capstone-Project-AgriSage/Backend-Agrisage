using System.Text.Json;

namespace AgriSage.UnitTests.Architecture;

// Committed appsettings files hold only non-secret values: the database password and JWT signing key come
// from User Secrets / environment variables / the gitignored appsettings.Local.json (README "Configuration & secrets").
public class CommittedConfigurationTests
{
    private const string LocalFile = "appsettings.Local.json";
    private const string TemplateFile = "appsettings.Local.example.json";

    // appsettings.Local.json is skipped: it is a developer's own gitignored file (LocalSettingsFileTests checks the
    // ignore rule), so it is never committed. The template is committed and is checked for empty values below.
    public static TheoryData<string> AppSettingsFiles => new(
        Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && Path.GetFileName(file) != LocalFile)
            .Select(file => Path.GetRelativePath(FindRepositoryRoot(), file)));

    [Theory]
    [MemberData(nameof(AppSettingsFiles))]
    public void Appsettings_contain_no_secret_values(string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath)));
        var root = document.RootElement;

        if (Path.GetFileName(relativePath) == TemplateFile)
        {
            // The template lists the secret keys on purpose, but every value must be empty.
            Assert.All(
                Leaves(root).Where(leaf => !leaf.Path.StartsWith('_')),
                leaf => Assert.True(string.IsNullOrEmpty(leaf.Value), $"{relativePath}: {leaf.Path} must be empty."));
            return;
        }

        if (root.TryGetProperty("Database", out var database))
        {
            Assert.False(database.TryGetProperty("Password", out _), $"{relativePath} must not contain Database:Password.");
        }

        Assert.False(root.TryGetProperty("ConnectionStrings", out _), $"{relativePath} must not contain a connection string.");

        if (root.TryGetProperty("Storage", out var storage))
        {
            Assert.False(storage.TryGetProperty("SecretKey", out _), $"{relativePath} must not contain Storage:SecretKey.");
        }

        if (root.TryGetProperty("AdminBootstrap", out var admin))
        {
            Assert.False(admin.TryGetProperty("Password", out _), $"{relativePath} must not contain AdminBootstrap:Password.");
        }

        if (root.TryGetProperty("Jwt", out var jwt) && jwt.TryGetProperty("SigningKey", out var signingKey))
        {
            Assert.Equal(string.Empty, signingKey.GetString());
        }
    }

    private static IEnumerable<(string Path, string? Value)> Leaves(JsonElement element, string path = "")
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var leaf in Leaves(property.Value, path.Length == 0 ? property.Name : $"{path}:{property.Name}"))
                {
                    yield return leaf;
                }
            }
        }
        else
        {
            yield return (path, element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString());
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
