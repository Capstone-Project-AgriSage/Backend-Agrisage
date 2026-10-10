using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Identity.Entities;

public sealed class Permission : BaseEntity
{
    private Permission() { }
    public Permission(Guid id, string code, string module, string name, bool isDelegable) : base(id)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        Module = Guard.NotNullOrWhiteSpace(module);
        Name = Guard.NotNullOrWhiteSpace(name);
        IsDelegable = isDelegable;
        IsActive = true;
    }
    public string Code { get; private set; } = null!;
    public string Module { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsDelegable { get; private set; }
    public bool IsActive { get; private set; }
}
