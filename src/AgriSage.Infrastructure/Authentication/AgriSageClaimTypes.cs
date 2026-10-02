namespace AgriSage.Infrastructure.Authentication;

// Short JWT claim names, used as-is (the Api sets MapInboundClaims = false).
public static class AgriSageClaimTypes
{
    public const string Subject = "sub";
    public const string Role = "role";
}
