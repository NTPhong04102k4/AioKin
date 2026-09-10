using System.Text.Json.Serialization;

namespace AioKin.Models.ViewModel.Auth.User;

/// <summary>Khuon token theo quy uoc OAuth2 (snake_case) de client dung chung mot bo parser.</summary>
public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>So giay con lai cua access token.</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    /// <summary>Role gan voi token — Customer, Staff, Admin hoac SuperAdmin.</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;
}
