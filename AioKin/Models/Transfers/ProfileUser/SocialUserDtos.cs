namespace AioKin.Models.Transfers.ProfileUser;

/// <summary>Ho so doc tu Google userinfo endpoint (hoac tu claim khi khong co access token).</summary>
public class GoogleUserDto
{
    public string IDSocial { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Picture { get; set; } = string.Empty;
    public bool VerifiedEmail { get; set; }
}

/// <summary>Ho so doc tu Facebook Graph API (hoac tu claim khi khong co access token).</summary>
public class FacebookUserDto
{
    public string IDSocial { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Picture { get; set; } = string.Empty;
    public string Birthday { get; set; } = string.Empty;
    public string Gender { get; set; } = "Other";
    public string Location { get; set; } = string.Empty;
    public string Hometown { get; set; } = string.Empty;
}
