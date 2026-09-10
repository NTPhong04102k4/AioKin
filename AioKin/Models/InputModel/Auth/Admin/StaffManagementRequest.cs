using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Auth.Admin;

public class StaffUpdateRequest
{
    [Required(ErrorMessage = "Ho ten la bat buoc.")]
    [MaxLength(200, ErrorMessage = "Ho ten toi da 200 ky tu.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email la bat buoc.")]
    [EmailAddress(ErrorMessage = "Email khong dung dinh dang.")]
    [MaxLength(100, ErrorMessage = "Email toi da 100 ky tu.")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "So dien thoai khong dung dinh dang.")]
    [MaxLength(25, ErrorMessage = "So dien thoai toi da 25 ky tu.")]
    public string? Phone { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "LocationID phai lon hon 0.")]
    public int LocationID { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "RoleID phai lon hon 0.")]
    public int RoleID { get; set; }
}

public class StaffStatusRequest
{
    public bool IsActive { get; set; }
}

public class UpdateStaffPasswordRequest
{
    /// <summary>Bat buoc khi doi cho chinh minh; SuperAdmin doi cho nguoi khac thi bo qua duoc.</summary>
    public string? CurrentPassword { get; set; }

    [Required(ErrorMessage = "Mat khau moi la bat buoc")]
    [MinLength(6, ErrorMessage = "Mat khau moi phai co it nhat 6 ky tu")]
    public string NewPassword { get; set; } = string.Empty;
}

public class SuperAdminRecoverPasswordRequest
{
    [Required(ErrorMessage = "Ma khoi phuc la bat buoc")]
    public string RecoveryCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mat khau moi la bat buoc")]
    [MinLength(6, ErrorMessage = "Mat khau moi phai co it nhat 6 ky tu")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xac nhan mat khau la bat buoc")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
