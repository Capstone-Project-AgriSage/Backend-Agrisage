using AgriSage.Api.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace AgriSage.IntegrationTests.Api;

// appsettings.Local.json: read only in Development, optional, never part of the repository or the build output.
public class LocalSettingsFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"agrisage-local-{Guid.NewGuid():N}");

    public LocalSettingsFileTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, LocalSettingsExtensions.FileName), """{ "Database": { "Password": "from-local-file" } }""");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private WebApplicationBuilder Builder(string environment) =>
        WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment, ContentRootPath = _directory });

    [Fact]
    public void The_file_is_read_in_development_and_overrides_other_sources()
    {
        var builder = Builder("Development");
        builder.Configuration["Database:Password"] = "from-another-source";

        builder.AddLocalSettingsFile();

        Assert.Equal("from-local-file", builder.Configuration["Database:Password"]);
    }

    [Fact]
    public void The_disabled_switch_skips_the_file_even_in_development()
    {
        var builder = Builder("Development");
        builder.Configuration[LocalSettingsExtensions.DisabledSetting] = "true";

        builder.AddLocalSettingsFile();

        Assert.Null(builder.Configuration["Database:Password"]);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void The_file_is_ignored_outside_development(string environment)
    {
        var builder = Builder(environment);

        builder.AddLocalSettingsFile();

        Assert.Null(builder.Configuration["Database:Password"]);
    }

    [Fact]
    public void A_missing_file_is_not_an_error()
    {
        File.Delete(Path.Combine(_directory, LocalSettingsExtensions.FileName));
        var builder = Builder("Development");

        builder.AddLocalSettingsFile();

        Assert.Null(builder.Configuration["Database:Password"]);
    }

    [Fact]
    public void The_template_has_only_empty_values_and_the_real_file_is_gitignored()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "AgriSage.sln")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        var template = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root!, "src", "AgriSage.Api", "appsettings.Local.example.json"))
            .Build();
        Assert.All(
            template.AsEnumerable().Where(pair => pair.Value is not null && !pair.Key.StartsWith('_')),
            pair => Assert.True(string.IsNullOrEmpty(pair.Value), $"{pair.Key} must be empty in the template"));

        var ignored = File.ReadAllLines(Path.Combine(root!, ".gitignore")).Select(line => line.Trim());
        Assert.Contains(LocalSettingsExtensions.FileName, ignored);
    }
}
