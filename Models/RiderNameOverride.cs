using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace LiveDahsboard.Models;

[Index(nameof(RiderId), nameof(CompanyId), IsUnique = true)]
public class RiderNameOverride
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string RiderId { get; set; } = null!;

    [Required, MaxLength(50)]
    public string CompanyId { get; set; } = null!;

    // Null means "no override — use whatever the API sends"
    [MaxLength(100)]
    public string? OverrideName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}