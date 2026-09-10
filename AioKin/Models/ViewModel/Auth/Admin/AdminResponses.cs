namespace AioKin.Models.ViewModel.Auth.Admin;

public class LoginAdminResponse
{
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
}

public class StaffResponse
{
    public string StaffCode { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class StaffManagementViewModel
{
    public int StaffID { get; set; }
    public string StaffCode { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public int LocationID { get; set; }
    public string? LocationName { get; set; }
    public int RoleID { get; set; }
    public string? RoleName { get; set; }
    public bool IsActive { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class StaffListResponse
{
    public IEnumerable<StaffManagementViewModel> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}
