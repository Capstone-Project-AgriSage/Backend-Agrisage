namespace AgriSage.Domain.Features.Products.Enums;

// product_packagings.status is a free varchar in the database design; these are the allowed values
// (database design §35.16). INACTIVE keeps the history of a packaging that is no longer sold or bought.
public static class PackagingStatus
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";

    public static bool IsValid(string? status) => status is Active or Inactive;
}
