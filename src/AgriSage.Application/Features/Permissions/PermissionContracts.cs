using FluentValidation;

namespace AgriSage.Application.Features.Permissions;

public sealed record CurrentPermissionsResponse(string Role, Guid? StoreId, long RoleVersion, long? MemberVersion, IReadOnlyList<string> Permissions);
public sealed record PermissionResponse(Guid Id, string Code, string Module, string Name, bool Delegable, IReadOnlyList<string> DefaultRoles, IReadOnlyList<string> AllowedRoles);
public sealed record RolePermissionsResponse(Guid Id, string Code, string Name, string? Description, long Version, bool Editable, IReadOnlyList<string> PermissionCodes);
public sealed record PermissionOverride(string Code, bool Granted);
public sealed record MemberPermissionsResponse(Guid UserId, string FullName, string Role, Guid StoreId, long Version, long RoleVersion,
    IReadOnlyList<string> DefaultPermissions, IReadOnlyList<string> EffectivePermissions, IReadOnlyList<PermissionOverride> Overrides, IReadOnlyList<string> GrantablePermissions);
public sealed record SetRolePermissionsRequest(IReadOnlyList<string> PermissionCodes, long Version, string Reason);
public sealed record SetMemberPermissionsRequest(IReadOnlyList<PermissionOverride> Overrides, long Version, long RoleVersion, string Reason);

public interface IPermissionEvaluator
{
    Task<CurrentPermissionsResponse> CurrentAsync(CancellationToken token);
    Task<bool> HasAsync(string code, CancellationToken token);
}
public interface IPermissionConfigurationService
{
    Task<IReadOnlyList<PermissionResponse>> CatalogAsync(CancellationToken token);
    Task<IReadOnlyList<RolePermissionsResponse>> RolesAsync(CancellationToken token);
    Task<RolePermissionsResponse> SetRoleAsync(Guid roleId, SetRolePermissionsRequest request, CancellationToken token);
    Task<MemberPermissionsResponse> MemberAsync(Guid userId, CancellationToken token);
    Task<MemberPermissionsResponse> SetMemberAsync(Guid userId, SetMemberPermissionsRequest request, CancellationToken token);
}
public interface IPermissionWriteLock { Task AcquireAsync(CancellationToken token); }

public sealed class SetRolePermissionsRequestValidator : AbstractValidator<SetRolePermissionsRequest>
{
    public SetRolePermissionsRequestValidator()
    {
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(500);
        RuleFor(r => r.PermissionCodes).NotNull().Must(v => v is null || v.Count <= PermissionCatalog.Entries.Count);
        RuleFor(r => r.PermissionCodes).Must(v => v is null || v.Count == v.Distinct(StringComparer.Ordinal).Count()).WithMessage("Duplicate permission codes.");
        RuleForEach(r => r.PermissionCodes).Must(c => c != null && PermissionCatalog.ByCode.ContainsKey(c)).WithMessage("Unknown permission code.");
    }
}
public sealed class SetMemberPermissionsRequestValidator : AbstractValidator<SetMemberPermissionsRequest>
{
    public SetMemberPermissionsRequestValidator()
    {
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
        RuleFor(r => r.RoleVersion).GreaterThanOrEqualTo(0);
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(500);
        RuleFor(r => r.Overrides).NotNull().Must(v => v is null || v.Count <= PermissionCatalog.Entries.Count);
        RuleFor(r => r.Overrides).Must(v => v is null || v.All(p => p != null) && v.Count == v.Select(p => p.Code).Distinct(StringComparer.Ordinal).Count())
            .WithMessage("Duplicate or null overrides.");
        RuleForEach(r => r.Overrides).Must(p => p is not null && p.Code is not null && PermissionCatalog.ByCode.ContainsKey(p.Code)).WithMessage("Unknown permission code.");
    }
}
