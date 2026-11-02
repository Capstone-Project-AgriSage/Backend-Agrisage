namespace AgriSage.Domain.Features.Debt.Enums;

// Reason codes of the debt policy (CS-11, "Công nợ theo vụ mùa" §4). Stored as UPPER_SNAKE text (MAT_MUA, ...).
public enum DebtReasonCode
{
    MatMua,       // poor harvest: pests, disease, low yield
    ThienTai,     // natural disaster: flood, salinity, storm
    KhoKhan,      // family hardship: illness, bereavement
    TriAn,        // goodwill for a long-standing customer, rounding off
    SaiSot,       // the store recorded the debt wrongly
    KhongThuHoi,  // customer unreachable or moved away for several seasons
    Khac          // anything else (the note explains it)
}

public static class DebtReasonCodes
{
    // Reducing or forgiving a debt (ADJUST, CANCEL) accepts every code.
    public static bool AllowsReduction(DebtReasonCode code) => Enum.IsDefined(code);

    // Extending a debt to a later season only makes sense for hardship (owner L3 choice 2026-11-03: two lists, one column).
    public static bool AllowsRollover(DebtReasonCode code) =>
        code is DebtReasonCode.MatMua or DebtReasonCode.ThienTai or DebtReasonCode.KhoKhan or DebtReasonCode.Khac;
}
