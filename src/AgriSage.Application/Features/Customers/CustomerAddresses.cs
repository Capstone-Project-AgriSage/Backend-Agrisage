using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Customers;

// Address reads shared by the Farmer (/api/me/addresses) and staff (/api/customers/{id}/addresses) views.
public sealed class CustomerAddresses(IAgriSageDbContext context)
{
    // Default first, then in creation order.
    public async Task<IReadOnlyList<AddressResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var addresses = await context.UserAddresses.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        return addresses.Select(Map).ToList();
    }

    public static AddressResponse Map(UserAddress a) => new(
        a.Id, a.RecipientName, a.RecipientPhone, a.AddressLine, a.Ward, a.District, a.Province, a.Latitude, a.Longitude,
        EnumText.Format(a.AddressType), a.IsDefault, a.CreatedAt);
}
