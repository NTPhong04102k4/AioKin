using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Family;

public class CreateInviteRequest
{
    /// <summary>So lan ma nay dung duoc. Toi da 50 — mot ma dung duoc vo han la mot ma vinh vien.</summary>
    [Range(1, 50)]
    public int MaxUses { get; set; } = 5;

    /// <summary>Han su dung, tinh bang gio. Toi da 30 ngay.</summary>
    [Range(1, 720)]
    public int ExpiresInHours { get; set; } = 24;
}
