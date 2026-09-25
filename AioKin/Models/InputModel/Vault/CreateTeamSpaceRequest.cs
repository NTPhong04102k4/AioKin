using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Vault;

public class CreateTeamSpaceRequest
{
    [Required(AllowEmptyStrings = false)]
    [MinLength(1)]
    [MaxLength(120)]
    public required string Name { get; set; }
}
