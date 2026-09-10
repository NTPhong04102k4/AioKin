using System.Globalization;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Auth.User;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Models.Transfers.ProfileUser;

/// <summary>
/// Chuyen doi giua entity va DTO. Viet tay thay vi dung AutoMapper: cac phep bien doi
/// o day (tach ho ten, sinh username tu email, doc ngay sinh Facebook) mang du kien
/// nghiep vu rieng, de doc va de test hon khi nam trong mot ham thuong.
/// </summary>
public static class UserMapper
{
    public static LoginResponse ToLoginResponse(UserDb user) => new()
    {
        UserID = user.UserUUID,
        FirstName = user.FirstName,
        LastName = user.LastName,
        FullName = user.FullName ?? $"{user.FirstName} {user.LastName}".Trim(),
        DateOfBirth = user.DateOfBirth,
        Gender = user.Gender,
        IdentityNumber = user.IdentityNumber,
        Phone = user.Phone,
        Email = user.Email,
        Address = user.Address,
        Username = user.Username,
        IsActive = user.IsActive,
        IsLocked = user.IsLocked,
        Image = user.Image,
        SocialProvider = user.SocialProvider,
        HasPassword = !string.IsNullOrEmpty(user.PasswordHash)
    };

    /// <summary>Tao user moi tu dang ky bang email/mat khau, sau khi OTP da xac thuc.</summary>
    public static UserDb FromRegistration(PendingRegistration registration)
    {
        var now = DateTime.UtcNow;
        return new UserDb
        {
            UserCode = $"USR_{now.Ticks}",
            Username = registration.Username,
            Email = registration.Email,
            PasswordHash = registration.PasswordHash,
            PasswordSalt = registration.PasswordSalt,
            EmailVerified = true,
            PhoneVerified = false,
            Gender = "Other",
            IsActive = true,
            IsLocked = false,
            LoginAttempts = 0,
            LastLoginDate = now,
            CreatedDate = now,
            UpdatedDate = now
        };
    }

    public static UserDb FromGoogle(GoogleUserDto dto)
    {
        var now = DateTime.UtcNow;
        return new UserDb
        {
            UserCode = $"GG_{Truncate(dto.IDSocial, 20)}_{now.Ticks}",
            Username = GenerateUsername(dto.Email, dto.IDSocial, now, "GG"),
            FirstName = ParseFirstName(dto.Name),
            LastName = ParseLastName(dto.Name),
            Email = NullIfBlank(dto.Email),
            EmailVerified = dto.VerifiedEmail,
            Image = NullIfBlank(dto.Picture),
            IDSocial = dto.IDSocial,
            SocialProvider = "google",
            Gender = "Other",
            IsActive = true,
            IsLocked = false,
            LoginAttempts = 0,
            LastLoginDate = now,
            CreatedDate = now,
            UpdatedDate = now
        };
    }

    public static UserDb FromFacebook(FacebookUserDto dto)
    {
        var now = DateTime.UtcNow;
        return new UserDb
        {
            UserCode = $"FB_{Truncate(dto.IDSocial, 20)}_{now.Ticks}",
            Username = GenerateUsername(dto.Email ?? dto.Name, dto.IDSocial, now, "FB"),
            FirstName = ParseFirstName(dto.Name),
            LastName = ParseLastName(dto.Name),
            // Facebook chi tra email khi nguoi dung cap quyen. De null thay vi chuoi rong,
            // neu khong moi tai khoan khong co email se dung chung mot gia tri va va cham
            // voi rang buoc unique tren cot email.
            Email = NullIfBlank(dto.Email),
            EmailVerified = false,
            Image = NullIfBlank(dto.Picture),
            IDSocial = dto.IDSocial,
            SocialProvider = "facebook",
            Gender = ParseGender(dto.Gender),
            Address = FirstNonEmpty(dto.Location, dto.Hometown),
            DateOfBirth = ParseFacebookBirthday(dto.Birthday),
            IsActive = true,
            IsLocked = false,
            LoginAttempts = 0,
            LastLoginDate = now,
            CreatedDate = now,
            UpdatedDate = now
        };
    }

    public static UserListItemResponse ToListItem(UserDb u) => new()
    {
        UserID = u.UserUUID,
        UserCode = u.UserCode,
        FirstName = u.FirstName,
        LastName = u.LastName,
        FullName = u.FullName,
        DateOfBirth = u.DateOfBirth,
        Gender = u.Gender,
        IdentityNumber = u.IdentityNumber,
        Phone = u.Phone,
        Email = u.Email,
        Address = u.Address,
        Username = u.Username,
        EmailVerified = u.EmailVerified,
        PhoneVerified = u.PhoneVerified,
        IDSocial = u.IDSocial,
        SocialProvider = u.SocialProvider,
        LastLoginDate = u.LastLoginDate,
        LoginAttempts = u.LoginAttempts,
        IsLocked = u.IsLocked,
        LockUntil = u.LockUntil,
        IsActive = u.IsActive,
        CreatedDate = u.CreatedDate,
        UpdatedDate = u.UpdatedDate,
        Image = u.Image
    };

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string? ParseFirstName(string? fullName)
    {
        var parts = SplitName(fullName);
        return parts.Length > 0 ? parts[0] : null;
    }

    private static string? ParseLastName(string? fullName)
    {
        var parts = SplitName(fullName);
        return parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : null;
    }

    private static string[] SplitName(string? fullName)
        => string.IsNullOrWhiteSpace(fullName)
            ? []
            : fullName.Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Username uu tien phan truoc dau @ cua email; khong co email thi ghep prefix voi
    /// social id. Van co the trung — nguoi goi phai xu ly loi unique tu database.
    /// </summary>
    private static string GenerateUsername(string? email, string? idSocial, DateTime timestamp, string prefix)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var local = email.Split('@')[0];
            if (!string.IsNullOrWhiteSpace(local))
                return Truncate(local, 50);
        }

        if (!string.IsNullOrWhiteSpace(idSocial))
            return $"{prefix}_{Truncate(idSocial, 20)}";

        return $"{prefix}_{timestamp:MMddHHmmssfff}";
    }

    private static DateTime? ParseFacebookBirthday(string? birthday)
    {
        if (string.IsNullOrWhiteSpace(birthday))
            return null;

        // Facebook tra ve MM/dd/yyyy khi nguoi dung chia se day du ngay sinh.
        if (DateTime.TryParseExact(birthday, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return DateTime.SpecifyKind(exact, DateTimeKind.Utc);

        return DateTime.TryParse(birthday, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback)
            ? DateTime.SpecifyKind(fallback, DateTimeKind.Utc)
            : null;
    }

    private static string ParseGender(string? gender) => gender?.ToLowerInvariant() switch
    {
        "male" => "Male",
        "female" => "Female",
        _ => "Other"
    };

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
