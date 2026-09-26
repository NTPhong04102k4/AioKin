namespace AioKin.Services.Auth.RefreshToken;

/// <summary>Du lieu luu kem refresh token trong Redis, duoi khoa sha256(token).</summary>
public sealed record RefreshTokenPayload(string Subject, string Role, string? DeviceId, string? DeviceName, string? Platform);
