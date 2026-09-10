namespace AioKin.Common;

/// <summary>Ten role dung xuyen suot JWT claim, [Authorize(Roles = ...)] va bang Security.Roles.</summary>
public static class Roles
{
    public const string SUPERADMIN = "SuperAdmin";
    public const string ADMIN = "Admin";
    public const string STAFF = "Staff";
    public const string CUSTOMER = "Customer";

    /// <summary>Cac role thuoc phia quan tri (dang nhap qua /auth/admin/login, tra bang Staff).</summary>
    public static readonly string[] StaffSide = [SUPERADMIN, ADMIN, STAFF];

    public static bool IsStaffSide(string? role)
        => role is not null && StaffSide.Contains(role, StringComparer.Ordinal);
}
