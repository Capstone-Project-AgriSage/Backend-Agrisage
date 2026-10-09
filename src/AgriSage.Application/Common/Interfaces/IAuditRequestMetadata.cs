namespace AgriSage.Application.Common.Interfaces;

public interface IAuditRequestMetadata
{
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}
