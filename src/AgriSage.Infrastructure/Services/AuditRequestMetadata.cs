using AgriSage.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace AgriSage.Infrastructure.Services;

public sealed class AuditRequestMetadata(IHttpContextAccessor accessor) : IAuditRequestMetadata
{
    public string? IpAddress => Trim(accessor.HttpContext?.Connection.RemoteIpAddress?.ToString(), 64);
    public string? UserAgent => Trim(accessor.HttpContext?.Request.Headers.UserAgent.ToString(), 1000);
    public string? CorrelationId => Trim(accessor.HttpContext?.TraceIdentifier, 100);
    private static string? Trim(string? value, int max) => string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, max)];
}
