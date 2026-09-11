using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Family;

public class JoinFamilyRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(16)]
    public required string Code { get; set; }
}
