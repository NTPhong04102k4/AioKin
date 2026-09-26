using AioKin.Data;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Vault;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Vault;

public class PromptBrowseService : IPromptBrowseService
{
    private readonly AioKinDbContext _db;
    private readonly ISpaceContext _spaceContext;

    public PromptBrowseService(AioKinDbContext db, ISpaceContext spaceContext)
    {
        _db = db;
        _spaceContext = spaceContext;
    }

    public async Task<OperationResult> ListAsync(Guid spaceUuid, CancellationToken cancellationToken = default)
    {
        var membership = await _spaceContext.ResolveAsync(spaceUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("Forbidden", "Ban khong thuoc space nay.");

        var prompts = await _db.Prompts
            .AsNoTracking()
            .Where(p => p.SpaceID == membership.SpaceID && !p.IsDeleted)
            .OrderByDescending(p => p.UpdatedDate)
            .Select(p => new PromptSummaryResponse
            {
                PromptId = p.PromptID,
                Title = p.Title,
                Description = p.Description,
                CategoryId = p.CategoryID,
                CategoryName = p.Category != null ? p.Category.Name : null,
                IsFavorite = p.IsFavorite,
                HasConflict = p.HasConflict,
                UpdatedAtMillis = new DateTimeOffset(DateTime.SpecifyKind(p.UpdatedDate, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            })
            .ToListAsync(cancellationToken);

        return OperationResult.Ok(data: prompts);
    }

    public async Task<OperationResult> GetAsync(Guid spaceUuid, Guid promptId, CancellationToken cancellationToken = default)
    {
        var membership = await _spaceContext.ResolveAsync(spaceUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("Forbidden", "Ban khong thuoc space nay.");

        var prompt = await _db.Prompts
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Variables)
            .Include(p => p.PromptTags).ThenInclude(pt => pt.Tag)
            .FirstOrDefaultAsync(p => p.SpaceID == membership.SpaceID && p.PromptID == promptId && !p.IsDeleted, cancellationToken);

        if (prompt is null)
            return OperationResult.Fail("NotFound", "Khong tim thay prompt.");

        return OperationResult.Ok(data: new PromptDetailResponse
        {
            PromptId = prompt.PromptID,
            Title = prompt.Title,
            Content = prompt.Content,
            Description = prompt.Description,
            CategoryId = prompt.CategoryID,
            CategoryName = prompt.Category?.Name,
            Version = prompt.Version,
            HasConflict = prompt.HasConflict,
            Tags = [.. prompt.PromptTags.Select(pt => pt.Tag!.Name)],
            Variables = [.. prompt.Variables.Select(v => new PromptVariableResponse { VarKey = v.VarKey, Label = v.Label, DefaultValue = v.DefaultValue })]
        });
    }

    // Expo gap G5.
    public async Task<OperationResult> ListCategoriesAsync(Guid spaceUuid, CancellationToken cancellationToken = default)
    {
        var membership = await _spaceContext.ResolveAsync(spaceUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("Forbidden", "Ban khong thuoc space nay.");

        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => c.SpaceID == membership.SpaceID)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryResponse { Id = c.CategoryID, Name = c.Name })
            .ToListAsync(cancellationToken);

        return OperationResult.Ok(data: categories);
    }

    // Expo gap G5.
    public async Task<OperationResult> ListTagsAsync(Guid spaceUuid, CancellationToken cancellationToken = default)
    {
        var membership = await _spaceContext.ResolveAsync(spaceUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("Forbidden", "Ban khong thuoc space nay.");

        var tags = await _db.Tags
            .AsNoTracking()
            .Where(t => t.SpaceID == membership.SpaceID)
            .OrderBy(t => t.Name)
            .Select(t => new TagResponse { Id = t.TagID, Name = t.Name })
            .ToListAsync(cancellationToken);

        return OperationResult.Ok(data: tags);
    }
}
