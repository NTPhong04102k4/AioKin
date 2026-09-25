namespace AioKin.Models.ViewModel.Vault;

/// <summary>
/// Dang rut gon cho danh sach prompt. CategoryId/CategoryName them theo Expo gap G5 — client
/// can hien thi ten category ngay tren list, khong muon goi rieng /prompts/categories cho tung
/// prompt.
/// </summary>
public class PromptSummaryResponse
{
    public Guid PromptId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public bool IsFavorite { get; set; }
    public bool HasConflict { get; set; }
    public long UpdatedAtMillis { get; set; }
}
