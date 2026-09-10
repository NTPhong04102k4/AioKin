namespace AioKin.Common;

/// <summary>
/// Issuer/Audience phai khop giua luc ky JWT va luc JwtBearer validate.
/// Ho tro <c>Jwt:Audience</c> la mot chuoi hoac mang (nhieu SPA / domain).
/// </summary>
public static class JwtConfiguration
{
    private const string DefaultIdentifier = "AioKinApi";

    public static string ResolveIssuer(IConfiguration config)
    {
        var iss = config["Jwt:Issuer"];
        return string.IsNullOrWhiteSpace(iss) ? DefaultIdentifier : iss.Trim();
    }

    /// <summary>Tat ca audience hop le khi validate token (mang hoac mot chuoi trong config).</summary>
    public static IReadOnlyList<string> ResolveAudiences(IConfiguration config)
    {
        var fromArray = config.GetSection("Jwt:Audience").Get<string[]>();
        if (fromArray is { Length: > 0 })
        {
            var list = fromArray
                .Where(static a => !string.IsNullOrWhiteSpace(a))
                .Select(static a => a.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (list.Length > 0)
                return list;
        }

        var single = config["Jwt:Audience"];
        if (!string.IsNullOrWhiteSpace(single))
            return [single.Trim()];

        return [ResolveIssuer(config)];
    }

    /// <summary>Mot gia tri <c>aud</c> khi ky JWT (JWT chi chua mot audience).</summary>
    public static string ResolveAudienceForSigning(IConfiguration config)
        => ResolveAudiences(config)[0];

    /// <summary>So phut song cua access token — dung chung cho ca luc ky va luc tinh TTL blacklist.</summary>
    public static int ResolveAccessTokenMinutes(IConfiguration config)
        => int.TryParse(config["Jwt:ExpiryMinutes"], out var m) && m > 0 ? m : 60;
}
