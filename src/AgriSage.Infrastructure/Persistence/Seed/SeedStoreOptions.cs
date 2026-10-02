namespace AgriSage.Infrastructure.Persistence.Seed;

public sealed class SeedStoreOptions
{
    public const string SectionName = "Seed:Store";

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string? Ward { get; set; }
    public string? District { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? TaxCode { get; set; }

    // Validate on an explicit seed invocation, never during ordinary HTTP startup.
    public void Validate()
    {
        Required(Code, nameof(Code), 30);
        Required(Name, nameof(Name), 200);
        Required(AddressLine, nameof(AddressLine), 500);
        Required(Province, nameof(Province), 150);
        Optional(Ward, nameof(Ward), 150);
        Optional(District, nameof(District), 150);
        Optional(PhoneNumber, nameof(PhoneNumber), 20);
        Optional(Email, nameof(Email), 255);
        Optional(TaxCode, nameof(TaxCode), 50);
    }

    private static void Required(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("<ĐIỀN>", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReferenceSeedException($"Seed:Store:{field} must be supplied and must not contain the <ĐIỀN> placeholder.");
        }

        Optional(value, field, maximumLength);
    }

    private static void Optional(string? value, string field, int maximumLength)
    {
        if (value?.Length > maximumLength)
        {
            throw new ReferenceSeedException($"Seed:Store:{field} exceeds its maximum length of {maximumLength}.");
        }
    }
}
