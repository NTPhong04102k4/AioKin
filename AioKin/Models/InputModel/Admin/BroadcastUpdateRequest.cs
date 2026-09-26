using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Admin;

public class BroadcastUpdateRequest
{
    [Required(ErrorMessage = "Phien ban la bat buoc.")]
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tieu de thong bao la bat buoc.")]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Noi dung thong bao la bat buoc.")]
    [MaxLength(1000)]
    public string Body { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ChangelogUrl { get; set; }
}
