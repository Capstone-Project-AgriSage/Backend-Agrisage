namespace AgriSage.Domain.Common;

// Single source of the rounding rules (coding rule #61, database design §35.1 / §35.10).
// Money inputs are never rounded: inputs with more than 2 decimals are rejected by Guard.Money.
public static class CostRounding
{
    public const int UnitCostDecimals = 6;
    public const int MoneyDecimals = 2;

    public static decimal RoundUnitCost(decimal value) =>
        Math.Round(value, UnitCostDecimals, MidpointRounding.AwayFromZero);

    // Only for money computed by division (Sales Return line value); multiply before dividing.
    public static decimal RoundMoney(decimal value) =>
        Math.Round(value, MoneyDecimals, MidpointRounding.AwayFromZero);
}
