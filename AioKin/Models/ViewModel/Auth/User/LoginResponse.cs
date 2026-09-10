namespace AioKin.Models.ViewModel.Auth.User;

/// <summary>Ho so nguoi dung tra ve sau khi dang nhap hoac khi doc /account/me.</summary>
public class LoginResponse
{
    public Guid UserID { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? IdentityNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsLocked { get; set; }
    public string? Image { get; set; }

    /// <summary>Nha cung cap SSO da tao tai khoan: google, facebook, hoac null neu dang ky bang mat khau.</summary>
    public string? SocialProvider { get; set; }

    /// <summary>False voi tai khoan tao tu SSO chua tung dat mat khau — client an muc "doi mat khau".</summary>
    public bool HasPassword { get; set; }
}
