namespace AgriSage.Api.Extensions;

// Browser clients (the React web app) call the API from another origin, so the API must say which origins may.
// Origins come from configuration (`Cors:AllowedOrigins`, an array: appsettings, or the environment variables
// Cors__AllowedOrigins__0, Cors__AllowedOrigins__1, ...). None configured = no origin is allowed (same-origin only), which is
// the default outside Development; appsettings.Development.json lists the local dev servers.
// The API uses bearer tokens in the Authorization header, not cookies, so credentials are not allowed and "*" is refused:
// every origin is listed explicitly.
public static class CorsExtensions
{
    public const string PolicyName = "AgriSageClients";

    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = ParseOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? []);

        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            }
        }));

        return services;
    }

    // Trims, drops empty entries and a trailing slash, removes duplicates (case-insensitive) and refuses anything that is not
    // an absolute http(s) origin without path, query or fragment: a wrong value must stop the start-up, not silently open or
    // close the API to a browser.
    public static string[] ParseOrigins(IEnumerable<string?> values)
    {
        var origins = new List<string>();

        foreach (var value in values.Select(v => v?.Trim().TrimEnd('/')).Where(v => !string.IsNullOrEmpty(v)))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.PathAndQuery != "/"
                || !string.IsNullOrEmpty(uri.Fragment)
                || value!.Contains('*'))
            {
                throw new InvalidOperationException(
                    $"Cors:AllowedOrigins contains '{value}'. Each entry must be an origin such as https://app.example.com " +
                    "(scheme, host and optional port; no path, no wildcard).");
            }

            if (!origins.Contains(value!, StringComparer.OrdinalIgnoreCase))
            {
                origins.Add(value!);
            }
        }

        return [.. origins];
    }
}
