namespace AioKin.Services.Auth.Biometric;

/// <summary>Du lieu tam luu trong Redis giua buoc challenge va verify.</summary>
public sealed record BiometricChallenge(string UserCode, string DeviceId, string Nonce);
