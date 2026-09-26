using AioKin.Data;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;

#pragma warning disable CS0618 // Type or member is obsolete in FirebaseAdmin

namespace AioKin.Services.Common.Notification;

/// <summary>
/// Ban cai thuc te cua IFcmNotificationService dung Google FirebaseAdmin SDK.
/// Ho tro multicast theo user, silent data-only, va tu dong prune cac token da chet (unregistered).
/// </summary>
public class FcmNotificationService(
    IServiceScopeFactory scopeFactory,
    ILogger<FcmNotificationService> logger) : IFcmNotificationService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<FcmNotificationService> _logger = logger;

    public async Task<bool> SendNotificationToUserAsync(
        Guid userId,
        string title,
        string body,
        Dictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        var tokens = await GetActiveTokensForUserAsync(userId, excludeDeviceId: null, ct);
        if (tokens.Count == 0)
        {
            _logger.LogDebug("User {UserId} khong co device token active nao de gui notification.", userId);
            return false;
        }

        var message = new MulticastMessage
        {
            Tokens = tokens.Select(t => t.Token).ToList(),
            Notification = new FirebaseAdmin.Messaging.Notification
            {
                Title = title,
                Body = body
            },
            Data = data
        };

        return await SendMulticastAndPruneAsync(message, tokens, ct);
    }

    public async Task<bool> SendDataOnlyToUserDevicesAsync(
        Guid userId,
        Dictionary<string, string> data,
        string? excludeDeviceId = null,
        CancellationToken ct = default)
    {
        var tokens = await GetActiveTokensForUserAsync(userId, excludeDeviceId, ct);
        if (tokens.Count == 0)
        {
            _logger.LogDebug("User {UserId} khong co thiet bi nao khac de danh thuc background sync.", userId);
            return false;
        }

        var message = new MulticastMessage
        {
            Tokens = tokens.Select(t => t.Token).ToList(),
            Data = data
        };

        return await SendMulticastAndPruneAsync(message, tokens, ct);
    }

    public async Task<bool> SendToTopicAsync(
        string topic,
        string title,
        string body,
        Dictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        try
        {
            var message = new Message
            {
                Topic = topic,
                Notification = new FirebaseAdmin.Messaging.Notification
                {
                    Title = title,
                    Body = body
                },
                Data = data
            };

            var response = await FirebaseMessaging.DefaultInstance.SendAsync(message, ct);
            _logger.LogInformation("Da gui thong bao toi topic {Topic}, message ID: {MessageId}", topic, response);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loi khi gui FCM notification toi topic {Topic}", topic);
            return false;
        }
    }

    private record DeviceTokenSnapshot(Guid DeviceTokenId, string Token, string? DeviceId);

    private async Task<List<DeviceTokenSnapshot>> GetActiveTokensForUserAsync(
        Guid userId,
        string? excludeDeviceId,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AioKinDbContext>();

        var query = db.DeviceTokens
            .Where(t => t.UserID == userId && t.IsActive);

        if (!string.IsNullOrWhiteSpace(excludeDeviceId))
        {
            query = query.Where(t => t.DeviceId != excludeDeviceId);
        }

        return await query
            .Select(t => new DeviceTokenSnapshot(t.DeviceTokenID, t.Token, t.DeviceId))
            .ToListAsync(ct);
    }

    private async Task<bool> SendMulticastAndPruneAsync(
        MulticastMessage message,
        List<DeviceTokenSnapshot> tokenSnapshots,
        CancellationToken ct)
    {
        try
        {
            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message, ct);
            _logger.LogInformation(
                "FCM multicast: Tong {Total}, Thanh cong: {Success}, That bai: {Failure}",
                response.Responses.Count, response.SuccessCount, response.FailureCount);

            if (response.FailureCount > 0)
            {
                var deadTokenIds = new List<Guid>();
                for (var i = 0; i < response.Responses.Count; i++)
                {
                    var item = response.Responses[i];
                    if (!item.IsSuccess && item.Exception is FirebaseMessagingException fex)
                    {
                        if (fex.MessagingErrorCode == MessagingErrorCode.Unregistered ||
                            fex.MessagingErrorCode == MessagingErrorCode.InvalidArgument)
                        {
                            deadTokenIds.Add(tokenSnapshots[i].DeviceTokenId);
                        }
                    }
                }

                if (deadTokenIds.Count > 0)
                {
                    await PruneDeadTokensAsync(deadTokenIds);
                }
            }

            return response.SuccessCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loi khi goi FirebaseMessaging.SendEachForMulticastAsync");
            return false;
        }
    }

    private async Task PruneDeadTokensAsync(List<Guid> deadTokenIds)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AioKinDbContext>();

            var tokensToDeactivate = await db.DeviceTokens
                .Where(t => deadTokenIds.Contains(t.DeviceTokenID))
                .ToListAsync();

            foreach (var token in tokensToDeactivate)
            {
                token.IsActive = false;
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("Da prune (deactivate) {Count} FCM tokens khong con hop le.", tokensToDeactivate.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loi khi prune dead tokens");
        }
    }
}
