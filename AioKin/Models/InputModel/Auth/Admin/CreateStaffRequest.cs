using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Auth.Admin;

public class CreateStaffRequest
{
    [Required(ErrorMessage = "Username la bat buoc.")]
    [MaxLength(50, ErrorMessage = "Username toi da 50 ky tu.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email la bat buoc.")]
    [EmailAddress(ErrorMessage = "Email khong dung dinh dang.")]
    [MaxLength(100, ErrorMessage = "Email toi da 100 ky tu.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mat khau la bat buoc.")]
    [MinLength(6, ErrorMessage = "Mat khau toi thieu 6 ky tu.")]
    [MaxLength(128, ErrorMessage = "Mat khau toi da 128 ky tu.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ho ten la bat buoc.")]
    [MaxLength(200, ErrorMessage = "Ho ten toi da 200 ky tu.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "So dien thoai la bat buoc.")]
    [Phone(ErrorMessage = "So dien thoai khong dung dinh dang.")]
    [MaxLength(25, ErrorMessage = "So dien thoai toi da 25 ky tu.")]
    public string Phone { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "LocationID phai lon hon 0.")]
    public int LocationID { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "RoleID phai lon hon 0.")]
    public int RoleID { get; set; }
}
