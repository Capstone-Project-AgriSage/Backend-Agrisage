namespace AgriSage.Application.Common.Interfaces;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
