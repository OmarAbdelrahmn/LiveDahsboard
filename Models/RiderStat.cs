using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiveDahsboard.Models;


[Index(nameof(CompanyId), nameof(Date))]                           // GET query index
[Index(nameof(RiderId), nameof(RiderName), nameof(Date), nameof(CompanyId), IsUnique = true)]  // Upsert lookup index
[Index(nameof(Date))]                                               // Date range scans
public class RiderStat
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string RiderId { get; set; } = null!;

    [Required, MaxLength(100)]
    public string RiderName { get; set; } = null!;

    [Required, MaxLength(50)]
    public string CompanyId { get; set; } = null!;

    public DateOnly Date { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Wallet { get; set; }

    public int Orders { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal WorkingHours { get; set; }

    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}