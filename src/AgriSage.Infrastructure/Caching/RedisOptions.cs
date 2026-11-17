using StackExchange.Redis;

namespace AgriSage.Infrastructure.Caching;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";
    public string? Url { get; set; }
    public string KeyPrefix { get; set; } = "agrisage:production:catalog:v1:";
    public int TtlSeconds { get; set; } = 120;
    public int TimeoutMilliseconds { get; set; } = 1500;

    public ConfigurationOptions? BuildConfiguration()
    {
        if (string.IsNullOrWhiteSpace(Url)) return null;
        // URLs copied from a terminal/chat can contain an escaped colon.
        if (!Uri.TryCreate(Url.Replace("\\:", ":"), UriKind.Absolute, out var uri)
            || uri.Scheme != "rediss" || string.IsNullOrWhiteSpace(uri.Host)
            || uri.AbsolutePath != "/" && uri.AbsolutePath != "" || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Redis:Url must be a valid rediss:// URL with TLS.");
        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2 || string.IsNullOrEmpty(credentials[0]) || string.IsNullOrEmpty(credentials[1]))
            throw new InvalidOperationException("Redis:Url requires a username and password.");
        return new ConfigurationOptions
        {
            EndPoints = { { uri.Host, uri.IsDefaultPort ? 6379 : uri.Port } },
            User = Uri.UnescapeDataString(credentials[0]), Password = Uri.UnescapeDataString(credentials[1]),
            Ssl = true, SslHost = uri.Host, AbortOnConnectFail = false,
            ConnectTimeout = Math.Clamp(TimeoutMilliseconds, 500, 5000),
            AsyncTimeout = Math.Clamp(TimeoutMilliseconds, 500, 5000), ConnectRetry = 0,
            BacklogPolicy = BacklogPolicy.FailFast, IncludeDetailInExceptions = false,
            ClientName = "AgriSage", AllowAdmin = false,
        };
    }
}
