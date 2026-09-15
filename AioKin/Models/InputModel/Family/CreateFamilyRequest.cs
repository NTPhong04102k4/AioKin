using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Family;

public class CreateFamilyRequest
{
    /// <summary>Ten hien thi cua gia dinh. Khong duoc de rong.</summary>
    [Required(AllowEmptyStrings = false)]
    [MinLength(1)]
    [MaxLength(120)]
    public required string Name { get; set; }
}
