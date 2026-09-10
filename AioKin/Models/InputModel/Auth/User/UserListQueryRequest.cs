using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Auth.User;

/// <summary>Tham so loc/phan trang cho danh sach nguoi dung phia quan tri.</summary>
public class UserListQueryRequest : IValidatableObject
{
    private static readonly string[] AllowedPeriods = ["7d", "30d", "all", "custom"];
    private static readonly string[] AllowedSortBy = ["createdDate", "updatedDate", "lastLoginDate", "username"];
    private static readonly string[] AllowedSortDir = ["asc", "desc"];

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string Period { get; set; } = "all";
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    /// <summary>Tim dong thoi tren email, username va phone.</summary>
    public string? Search { get; set; }

    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? Phone { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsLocked { get; set; }
    public string SortBy { get; set; } = "createdDate";
    public string SortDir { get; set; } = "desc";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!AllowedPeriods.Contains(Period, StringComparer.OrdinalIgnoreCase))
            yield return new ValidationResult("period must be one of: 7d, 30d, all, custom.", [nameof(Period)]);

        if (!AllowedSortBy.Contains(SortBy, StringComparer.OrdinalIgnoreCase))
            yield return new ValidationResult("sortBy must be one of: createdDate, updatedDate, lastLoginDate, username.", [nameof(SortBy)]);

        if (!AllowedSortDir.Contains(SortDir, StringComparer.OrdinalIgnoreCase))
            yield return new ValidationResult("sortDir must be asc or desc.", [nameof(SortDir)]);

        if (FromDate.HasValue && ToDate.HasValue && FromDate.Value > ToDate.Value)
            yield return new ValidationResult("fromDate must be before or equal to toDate.", [nameof(FromDate), nameof(ToDate)]);
    }
}

/// <summary>Khoa/mo khoa mot tai khoan khach hang tu phia quan tri.</summary>
public class UserLockRequest
{
    public bool IsLocked { get; set; }

    /// <summary>Thoi diem het khoa. Bo trong = khoa vo thoi han cho toi khi admin mo lai.</summary>
    public DateTime? LockUntil { get; set; }
}

/// <summary>Bat/tat tai khoan khach hang.</summary>
public class UserStatusRequest
{
    public bool IsActive { get; set; }
}
