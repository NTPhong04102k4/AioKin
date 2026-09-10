using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Core;

/// <summary>Chi nhanh / dia diem lam viec ma mot Staff duoc gan vao.</summary>
[Table("locations", Schema = "core")]
public class Location
{
    [Key]
    public int LocationID { get; set; }

    [MaxLength(20)]
    public required string LocationCode { get; set; }

    [MaxLength(200)]
    public required string LocationName { get; set; }

    /// <summary>Store / Workshop / Warehouse / Office.</summary>
    [MaxLength(50)]
    public string LocationType { get; set; } = "Office";

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Province { get; set; }

    [MaxLength(25)]
    public string? Phone { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
