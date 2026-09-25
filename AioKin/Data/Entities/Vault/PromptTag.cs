using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Vault;

/// <summary>
/// Nhieu-nhieu Prompt-Tag. KHONG phai mot entity_type rieng trong sync_log — dong bo nhu
/// mot phan cua payload Prompt (spec muc 6, sync_log.entity_type khong co 'prompt_tag').
///
/// Ruling (Med, progress.md): can [Table(..., Schema="vault")] rieng cho bang join nay —
/// brief goc thieu attribute nay, chi dua vao cau hinh HasKey trong DbContext.
/// </summary>
[Table("prompt_tags", Schema = "vault")]
public class PromptTag
{
    public Guid PromptID { get; set; }
    public Prompt? Prompt { get; set; }

    public Guid TagID { get; set; }
    public Tag? Tag { get; set; }
}
