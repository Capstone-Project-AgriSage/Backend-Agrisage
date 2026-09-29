using AgriSage.Application.Common.Interfaces;

namespace AgriSage.Infrastructure.Services;

public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
