using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AioKin.Data.Entities.Core;

namespace AioKin.Data.Entities.Security;

/// <summary>
/// Tai khoan phia quan tri (Staff / Admin / SuperAdmin). Tach hoan toan khoi
/// <see cref="User"/>: nhan vien chi dang nhap bang username/password, khong co SSO.
/// </summary>
[Table("staff", Schema = "security")]
public class Staff
{
    [Key]
    public int StaffID { get; set; }

    [MaxLength(20)]
    public required string StaffCode { get; set; }

    [MaxLength(50)]
    public required string Username { get; set; }

    [MaxLength(255)]
    public required string PasswordHash { get; set; }

    [MaxLength(255)]
    public required string PasswordSalt { get; set; }

    [MaxLength(200)]
    public required string FullName { get; set; }

    [MaxLength(100)]
    public required string Email { get; set; }

    [MaxLength(25)]
    public string? Phone { get; set; }

    public int LocationID { get; set; }

    [ForeignKey(nameof(LocationID))]
    public Location? Location { get; set; }

    public int RoleID { get; set; }

    [ForeignKey(nameof(RoleID))]
    public Role? Role { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>StaffID cua nguoi tao. 0 = tai khoan bootstrap do he thong seed.</summary>
    public int CreatedBy { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
