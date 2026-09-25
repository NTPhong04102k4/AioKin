namespace AioKin.Services.Auth.Token;

public enum AccessTokenSubjectKind { Customer, Staff }

/// <summary>
/// Du lieu can de dung lai ClaimsPrincipal tu mot access token opaque, luu trong Redis
/// duoi khoa sha256(token). Thay the hoan toan cho claim ben trong JWT cu — khong thieu
/// truong nao ma JwtTokenService tung ghi.
/// </summary>
public sealed class AccessTokenSession
{
    public required AccessTokenSubjectKind Kind { get; init; }

    /// <summary>Khoa dung cho RevokeAllForSubjectAsync — UserCode (Customer) hoac Username (Staff).</summary>
    public required string Subject { get; init; }

    public Guid? UserUuid { get; init; }
    public int? StaffId { get; init; }
    public required string Username { get; init; }
    public string? Email { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }

    public string? DeviceId { get; init; }
    public string? DeviceName { get; init; }
    public string? Platform { get; init; }

    public long IssuedAtUnix { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
