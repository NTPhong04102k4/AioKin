namespace AioKin.Common;

/// <summary>Cau hinh lien quan den access token opaque — tuoi tho doc tu Jwt:ExpiryMinutes.</summary>
public static class JwtConfiguration
{
    /// <summary>So phut song cua access token.</summary>
    public static int ResolveAccessTokenMinutes(IConfiguration config)
        => int.TryParse(config["Jwt:ExpiryMinutes"], out var m) && m > 0 ? m : 60;
}
