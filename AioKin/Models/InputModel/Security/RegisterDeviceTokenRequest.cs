using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Security;

public class RegisterDeviceTokenRequest
{
    [Required(ErrorMessage = "FCM token la bat buoc.")]
    [MaxLength(512)]
    public string Token { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Platform { get; set; } = "android";

    [MaxLength(100)]
    public string? DeviceId { get; set; }
}
