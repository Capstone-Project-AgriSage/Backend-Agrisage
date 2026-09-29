using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Identity.Entities;

namespace AgriSage.Domain.Features.Customers.Entities;

// Registered customer identity. "Owning user must have role FARMER" is checked by Application.
public sealed class FarmerProfile : SoftDeletableEntity
{
    private FarmerProfile()
    {
    }

    public FarmerProfile(Guid userId, DateOnly? dateOfBirth = null, string? gender = null, string? notes = null)
    {
        UserId = userId;
        UpdateDetails(dateOfBirth, gender, notes);
    }

    public Guid UserId { get; private set; }

    public User User { get; private set; } = null!;

    public DateOnly? DateOfBirth { get; private set; }

    // Allowed values are not defined by the database design yet.
    public string? Gender { get; private set; }

    public string? Notes { get; private set; }

    public void UpdateDetails(DateOnly? dateOfBirth, string? gender, string? notes)
    {
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Notes = notes;
    }
}
