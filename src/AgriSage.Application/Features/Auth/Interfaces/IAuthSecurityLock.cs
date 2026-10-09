namespace AgriSage.Application.Features.Auth.Interfaces;

// Call inside the Application-owned transaction; user first, then session/outbox rows.
public interface IAuthSecurityLock
{
    Task LockUserAsync(Guid id, CancellationToken token);
    Task LockSessionAsync(Guid id, CancellationToken token);
}
