using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Security;

/// <summary>Role cua nhan vien. RoleName khop hang so trong <see cref="AioKin.Common.Roles"/>.</summary>
[Table("roles", Schema = "security")]
public class Role
{
    [Key]
    public int RoleID { get; set; }

    [MaxLength(50)]
    public required string RoleName { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }

    /// <summary>JSON array cac permission. De rong <c>[]</c> neu chua phan quyen chi tiet.</summary>
    public string Permissions { get; set; } = "[]";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public ICollection<Staff> Staffs { get; set; } = [];
}
