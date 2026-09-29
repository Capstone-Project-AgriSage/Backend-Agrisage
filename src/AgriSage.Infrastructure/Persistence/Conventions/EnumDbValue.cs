using System.Text.RegularExpressions;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AgriSage.Infrastructure.Persistence.Conventions;

// Database string of every Domain enum value: documented UPPER_SNAKE_CASE (database design §0.5).
// PascalCase names convert automatically; the few names that differ from the documented value are overridden here.
public static partial class EnumDbValue
{
    private static readonly Dictionary<Enum, string> Overrides = new()
    {
        [PaymentMethod.PayOs] = "PAYOS",
        [PaymentConfirmationSource.PayOsWebhook] = "PAYOS_WEBHOOK"
    };

    public static string ToDb(Enum value) =>
        Overrides.TryGetValue(value, out var documented) ? documented : ToUpperSnake(value.ToString());

    public static IReadOnlyList<string> AllValues(Type enumType) =>
        Enum.GetValues(enumType).Cast<Enum>().Select(ToDb).ToList();

    private static string ToUpperSnake(string name) => WordBoundary().Replace(name, "_$1").ToUpperInvariant();

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex WordBoundary();
}

// Enum ↔ documented UPPER_SNAKE string, applied to every enum-typed property (also nullable ones).
public sealed class UpperSnakeEnumConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> ToProviderMap =
        Enum.GetValues<TEnum>().ToDictionary(value => value, value => EnumDbValue.ToDb(value));

    private static readonly Dictionary<string, TEnum> FromProviderMap =
        ToProviderMap.ToDictionary(pair => pair.Value, pair => pair.Key);

    public UpperSnakeEnumConverter()
        : base(value => ToProvider(value), value => FromProvider(value))
    {
    }

    private static string ToProvider(TEnum value) => ToProviderMap[value];

    private static TEnum FromProvider(string value) => FromProviderMap[value];
}
