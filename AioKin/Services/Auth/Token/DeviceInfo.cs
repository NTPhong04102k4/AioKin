namespace AioKin.Services.Auth.Token;

/// <summary>
/// Thiet bi phat token. DeviceId co the null tu client cu chua cap nhat — Resolve() sinh
/// mot id tam thoi de phien van track duoc rieng, chi khong hien thi ten/nen tang.
/// </summary>
public sealed record DeviceInfo(string? DeviceId, string? DeviceName, string? Platform)
{
    public static readonly DeviceInfo Unknown = new(null, null, null);

    public static DeviceInfo Resolve(string? deviceId, string? deviceName, string? platform)
        => new(string.IsNullOrWhiteSpace(deviceId) ? $"unknown-{Guid.NewGuid():N}" : deviceId, deviceName, platform);
}
