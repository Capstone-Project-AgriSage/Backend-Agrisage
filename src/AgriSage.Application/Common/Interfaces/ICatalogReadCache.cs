namespace AgriSage.Application.Common.Interfaces;

// Shared catalog DTOs only. Authorization still runs normally; no user, price or stock data.
public interface ICatalogReadCache
{
    Task<T> GetOrCreateAsync<T>(string area, object parameters, Func<Task<T>> load,
        CancellationToken cancellationToken) where T : class;
}
