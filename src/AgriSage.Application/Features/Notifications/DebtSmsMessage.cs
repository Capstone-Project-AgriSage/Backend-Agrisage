using System.Globalization;

namespace AgriSage.Application.Features.Notifications;

internal static class DebtSmsMessage
{
    public static string Create(string milestone, decimal amount, DateOnly due)
    {
        var label = milestone switch
        {
            "N1" => "Mua thu hoach da bat dau",
            "N2" => "Khoan no sap den han",
            "N3" => "Khoan no den han",
            _ => "Khoan no qua han",
        };
        return $"AgriSage: {label}. So tien {amount.ToString("N0", CultureInfo.InvariantCulture)} VND, han {due:dd/MM/yyyy}. Vui long thanh toan hoac lien he cua hang.";
    }
}
