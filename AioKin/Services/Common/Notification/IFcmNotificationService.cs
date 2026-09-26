namespace AioKin.Services.Common.Notification;

/// <summary>
/// Dich vu gui thong bao Firebase Cloud Messaging (FCM) toi thiet bi va topic.
/// </summary>
public interface IFcmNotificationService
{
    /// <summary>Gui push notification co giao dien (title, body) toi cac thiet bi cua mot user.</summary>
    Task<bool> SendNotificationToUserAsync(Guid userId, string title, string body, Dictionary<string, string>? data = null, CancellationToken ct = default);

    /// <summary>Gui silent data-only message toi cac thiet bi cua mot user de danh thuc background sync.</summary>
    Task<bool> SendDataOnlyToUserDevicesAsync(Guid userId, Dictionary<string, string> data, string? excludeDeviceId = null, CancellationToken ct = default);

    /// <summary>Phat thong bao toi mot topic (vi du topic 'app-updates' cho phien ban moi).</summary>
    Task<bool> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null, CancellationToken ct = default);
}
