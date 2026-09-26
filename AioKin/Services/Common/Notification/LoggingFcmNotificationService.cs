namespace AioKin.Services.Common.Notification;

/// <summary>
/// Ban cai gia lap ghi log thay vi gui FCM that, dung khi chua cau hinh Firebase credential
/// tren moi truong Dev/Test.
/// </summary>
public class LoggingFcmNotificationService(ILogger<LoggingFcmNotificationService> logger) : IFcmNotificationService
{
    private readonly ILogger<LoggingFcmNotificationService> _logger = logger;

    public Task<bool> SendNotificationToUserAsync(Guid userId, string title, string body, Dictionary<string, string>? data = null, CancellationToken ct = default)
    {
        _logger.LogInformation("[MockFCM] Gui notification toi User {UserId}: Title='{Title}', Body='{Body}', DataCount={DataCount}",
            userId, title, body, data?.Count ?? 0);
        return Task.FromResult(true);
    }

    public Task<bool> SendDataOnlyToUserDevicesAsync(Guid userId, Dictionary<string, string> data, string? excludeDeviceId = null, CancellationToken ct = default)
    {
        _logger.LogInformation("[MockFCM] Gui data-only message toi User {UserId} (loai tru device {ExcludeDevice}): Keys={Keys}",
            userId, excludeDeviceId ?? "none", string.Join(",", data.Keys));
        return Task.FromResult(true);
    }

    public Task<bool> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null, CancellationToken ct = default)
    {
        _logger.LogInformation("[MockFCM] Phat notification toi Topic '{Topic}': Title='{Title}', Body='{Body}'",
            topic, title, body);
        return Task.FromResult(true);
    }
}
