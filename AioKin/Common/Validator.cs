using System.Text.RegularExpressions;

namespace AioKin.Common;

public static partial class Validator
{
    [GeneratedRegex(@"^(09|03|07|08|05)\d{8}$")]
    private static partial Regex VietnamesePhoneRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    /// <summary>Dau so di dong Viet Nam: 09, 03, 07, 08, 05 + 8 chu so.</summary>
    public static bool IsValidVietnamesePhone(string? phone)
        => !string.IsNullOrWhiteSpace(phone) && VietnamesePhoneRegex().IsMatch(phone);

    public static bool IsValidEmail(string? email)
        => !string.IsNullOrWhiteSpace(email) && EmailRegex().IsMatch(email);
}
