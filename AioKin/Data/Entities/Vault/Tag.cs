using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Vault;

/// <summary>
/// Id client tu sinh — cung ly do voi Category, xem ghi chu trong Category.cs.
/// </summary>
[Table("tags", Schema = "vault")]
public class Tag
{
    public const string SubjectType = "Tag";

    [Key]
    public Guid TagID { get; set; }

    public Guid SpaceID { get; set; }

    [ForeignKey(nameof(SpaceID))]
    public Space? Space { get; set; }

    [MaxLength(50)]
    public required string Name { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
