using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Vault;

[Table("prompt_variables", Schema = "vault")]
public class PromptVariable
{
    public const string SubjectType = "PromptVariable";

    [Key]
    public Guid VariableID { get; set; }

    public Guid PromptID { get; set; }

    [ForeignKey(nameof(PromptID))]
    public Prompt? Prompt { get; set; }

    [MaxLength(50)]
    public required string VarKey { get; set; }

    [MaxLength(100)]
    public string? Label { get; set; }

    [MaxLength(500)]
    public string? DefaultValue { get; set; }

    [MaxLength(20)]
    public string VarType { get; set; } = "text";

    /// <summary>JSON, chi dung khi VarType = "select": ["A","B","C"].</summary>
    public string? Options { get; set; }

    public int SortOrder { get; set; }
}
