using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Vault;

public class AddSpaceMemberRequest
{
    /// <summary>UserCode cua nguoi duoc them — khong nhan UserUUID tu client de tranh do doan.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string UserCode { get; set; }
}
