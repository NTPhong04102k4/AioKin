using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Auth.User;

/// <summary>
/// Cap nhat ho so. Moi truong deu optional — chi truong duoc gui len moi bi ghi de,
/// nen client co the PATCH tung phan ma khong lam mat du lieu con lai.
/// </summary>
public class UpdateProfileRequest
{
    [MaxLength(50)]
    public string? Username { get; set; }

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [EmailAddress]
    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(25)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(20)]
    public string? IdentityNumber { get; set; }

    [MaxLength(10)]
    public string? Gender { get; set; }

    public DateTime? DateOfBirth { get; set; }

    /// <summary>URL anh dai dien da duoc host o noi khac.</summary>
    [MaxLength(500)]
    public string? Image { get; set; }
}
