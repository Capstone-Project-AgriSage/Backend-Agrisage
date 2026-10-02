using AgriSage.Application.Features.Auth.Dtos.Requests;

namespace AgriSage.Application.Features.Auth.Interfaces;

public enum AdminBootstrapResult
{
    Created,
    AlreadyExists
}

// Creates the first Admin account (command --create-admin). Idempotent.
public interface IAdminBootstrapService
{
    Task<AdminBootstrapResult> CreateFirstAdminAsync(CreateAdminRequest request, CancellationToken cancellationToken);
}
