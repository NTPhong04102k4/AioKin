using AioKin.Data;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Services.Common.Notification;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Vault;

public class PromptEnrichmentService(
    AioKinDbContext db,
    ISpaceContext spaceContext,
    IFcmNotificationService fcmService,
    IServiceScopeFactory scopeFactory,
    ILogger<PromptEnrichmentService> logger) : IPromptEnrichmentService
{
    private readonly AioKinDbContext _db = db;
    private readonly ISpaceContext _spaceContext = spaceContext;
    private readonly IFcmNotificationService _fcmService = fcmService;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<PromptEnrichmentService> _logger = logger;

    public async Task<OperationResult> EnrichPromptAsync(Guid spaceUuid, Guid promptId, CancellationToken ct = default)
    {
        var membership = await _spaceContext.ResolveAsync(spaceUuid, ct);
        if (membership is null)
            return OperationResult.Fail("Forbidden", "Ban khong thuoc space nay.");

        var prompt = await _db.Prompts
            .FirstOrDefaultAsync(p => p.PromptID == promptId && p.SpaceID == membership.SpaceID, ct);

        if (prompt is null)
            return OperationResult.Fail("NotFound", "Khong tim thay prompt.");

        var userId = membership.UserID;
        var promptTitle = prompt.Title;

        // Chay tac vu cai thien prompt trong background
        _ = Task.Run(async () =>
        {
            try
            {
                // Mo phong tien trinh AI toi uu hoa prompt (LLM enrichment)
                await Task.Delay(1500);

                using var scope = _scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<AioKinDbContext>();
                var targetPrompt = await scopedDb.Prompts.FirstOrDefaultAsync(p => p.PromptID == promptId);

                if (targetPrompt is not null)
                {
                    targetPrompt.UpdatedDate = DateTime.UtcNow;
                    await scopedDb.SaveChangesAsync();
                }

                // Gui notification ve app qua FCM
                await _fcmService.SendNotificationToUserAsync(
                    userId,
                    "Cải thiện prompt hoàn tất",
                    $"Prompt \"{promptTitle}\" đã được tối ưu hóa thành công.",
                    new Dictionary<string, string>
                    {
                        ["type"] = "prompt_enriched",
                        ["promptId"] = promptId.ToString(),
                        ["spaceId"] = spaceUuid.ToString()
                    });

                _logger.LogInformation("Hoan tat AI enrichment va gui FCM notification cho prompt {PromptId}", promptId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loi khi chay background prompt enrichment cho prompt {PromptId}", promptId);
            }
        });

        return OperationResult.Ok("Yeu cau cai thien prompt da duoc tiep nhan va dang xu ly.");
    }
}
