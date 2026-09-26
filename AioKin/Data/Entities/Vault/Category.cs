using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Vault;

/// <summary>
/// Id client tu sinh (khong phai server) — xem Global Constraints cua plan nay: 4 entity
/// nay la nhom duy nhat can id on dinh truoc khi cham server, phuc vu offline sync.
/// </summary>
[Table("categories", Schema = "vault")]
public class Category
{
    public const string SubjectType = "Category";

    [Key]
    public Guid CategoryID { get; set; }

    public Guid SpaceID { get; set; }

    [ForeignKey(nameof(SpaceID))]
    public Space? Space { get; set; }

    [MaxLength(80)]
    public required string Name { get; set; }

    [MaxLength(50)]
    public string? Icon { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
