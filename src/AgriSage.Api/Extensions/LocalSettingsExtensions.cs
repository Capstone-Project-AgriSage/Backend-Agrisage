namespace AgriSage.Api.Extensions;

// Optional per-developer settings file for secrets, as an alternative to User Secrets. The file is gitignored, excluded
// from publish (see the csproj) and read only in Development, so it can never reach git or a server; real
// environments use environment variables. It is added last, so in Development it overrides User Secrets and
// environment variables.
public static class LocalSettingsExtensions
{
    public const string FileName = "appsettings.Local.json";

    public static WebApplicationBuilder AddLocalSettingsFile(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddJsonFile(FileName, optional: true, reloadOnChange: true);
        }

        return builder;
    }
}
