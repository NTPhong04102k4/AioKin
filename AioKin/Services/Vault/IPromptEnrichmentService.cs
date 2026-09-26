using AioKin.Models.InputModel.Auth.User;

namespace AioKin.Services.Vault;

public interface IPromptEnrichmentService
{
    Task<OperationResult> EnrichPromptAsync(Guid spaceUuid, Guid promptId, CancellationToken ct = default);
}
