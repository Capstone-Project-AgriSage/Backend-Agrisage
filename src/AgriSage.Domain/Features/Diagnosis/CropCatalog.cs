namespace AgriSage.Domain.Features.Diagnosis;

// The crops the store advises on (AI_DIAGNOSIS.md 2.2). A crop's code is the value of diseases.crop_type and
// diagnosis_cases.crop_type; a crop is offered for photo diagnosis once it has at least one active disease.
public static class CropCatalog
{
    public sealed record Crop(string Code, string Name, int Order);

    public const string Rice = "RICE";
    public const string Durian = "DURIAN";
    public const string Coffee = "COFFEE";
    public const string Corn = "CORN";
    public const string Mango = "MANGO";

    public static IReadOnlyList<Crop> All { get; } =
    [
        new(Rice, "Lúa", 1),
        new(Durian, "Sầu riêng", 2),
        new(Coffee, "Cà phê", 3),
        new(Corn, "Bắp (ngô)", 4),
        new(Mango, "Xoài", 5),
    ];

    private static readonly Dictionary<string, Crop> ByCode = All.ToDictionary(c => c.Code, StringComparer.Ordinal);

    public static bool IsKnown(string? code) => code is not null && ByCode.ContainsKey(code);

    public static Crop? Find(string? code) => code is not null && ByCode.TryGetValue(code, out var crop) ? crop : null;

    /// <summary>Upper-cases and trims what a client sent ("durian " → "DURIAN"); null or blank stays null.</summary>
    public static string? Normalize(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
}
