namespace AioKin.Models.ViewModel.Auth.User;

public class UserListItemResponse
{
    public Guid UserID { get; set; }
    public string? UserCode { get; set; }
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
    public bool EmailVerified { get; set; }
    public bool PhoneVerified { get; set; }
    public string? IDSocial { get; set; }
    public string? SocialProvider { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public int LoginAttempts { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? LockUntil { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime UpdatedDate { get; set; }
    public string? Image { get; set; }
}

public class UserListResponse
{
    public IEnumerable<UserListItemResponse> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
    public string Period { get; set; } = "all";
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Search { get; set; }
}
