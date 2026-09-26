using System.Text.Json.Serialization;

namespace AioKin.Models.ViewModel.Auth.User;

/// <summary>Mot access session dang song, hien thi trong GET /account/sessions.</summary>
public class SessionResponse
{
    /// <summary>12 ky tu dau cua sha256(token) — khong the dao nguoc ve token that.</summary>
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    public string? Platform { get; set; }

    [JsonPropertyName("issuedAt")]
    public long IssuedAtUnix { get; set; }

    public bool IsCurrent { get; set; }
}
