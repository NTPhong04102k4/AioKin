namespace AioKin.Models.ViewModel.Vault;

/// <summary>
/// Dang day du cho 1 prompt. CategoryId/CategoryName them theo Expo gap G5, cung ly do voi
/// PromptSummaryResponse.
/// </summary>
public class PromptDetailResponse
{
    public Guid PromptId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int Version { get; set; }
    public bool HasConflict { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<PromptVariableResponse> Variables { get; set; } = [];
}

public class PromptVariableResponse
{
    public string VarKey { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? DefaultValue { get; set; }
}
