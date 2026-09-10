using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Security;

/// <summary>
/// Tai khoan khach hang. Ba duong dang nhap deu do vao day:
/// username/password (PasswordHash + PasswordSalt), Google va Facebook (IDSocial).
/// Tai khoan tao tu OAuth khong co PasswordHash cho toi khi nguoi dung tu dat mat khau.
/// </summary>
[Table("users", Schema = "security")]
public class User
{
    [Key]
    public Guid UserID { get; set; } = Guid.NewGuid();

    /// <summary>Id cong khai — dung trong claim <c>sub</c> va moi API. Khong lo UserID noi bo.</summary>
    public Guid UserUUID { get; set; } = Guid.NewGuid();

    [MaxLength(50)]
    public required string UserCode { get; set; }

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    /// <summary>Cot sinh boi database (GENERATED ALWAYS ... STORED) — xem AioKinDbContext.</summary>
    [MaxLength(205)]
    public string? FullName { get; private set; }

    public DateTime? DateOfBirth { get; set; }

    /// <summary>Male / Female / Other.</summary>
    [MaxLength(10)]
    public string? Gender { get; set; }

    [MaxLength(20)]
    public string? IdentityNumber { get; set; }

    [MaxLength(25)]
    public string? Phone { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public required string Username { get; set; }

    [MaxLength(255)]
    public string? PasswordHash { get; set; }

    [MaxLength(255)]
    public string? PasswordSalt { get; set; }

    public bool EmailVerified { get; set; }

    public bool PhoneVerified { get; set; }

    /// <summary>Subject id ben Google/Facebook. Null voi tai khoan dang ky bang mat khau.</summary>
    [MaxLength(100)]
    public string? IDSocial { get; set; }

    /// <summary>Nha cung cap SSO da tao tai khoan nay: <c>google</c>, <c>facebook</c>, hoac null.</summary>
    [MaxLength(20)]
    public string? SocialProvider { get; set; }

    public DateTime? LastLoginDate { get; set; }

    public int LoginAttempts { get; set; }

    public bool IsLocked { get; set; }

    public DateTime? LockUntil { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Image { get; set; }
}
