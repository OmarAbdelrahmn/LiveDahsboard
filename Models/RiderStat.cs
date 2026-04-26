using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiveDahsboard.Models;



[Index(nameof(CompanyId), nameof(Date))]
[Index(nameof(RiderId), nameof(RiderName), nameof(Date), nameof(CompanyId), IsUnique = true)]
[Index(nameof(Date))]
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

    // ── NEW: shift accumulation fields ──────────────────────
    // These are internal tracking fields, never exposed in DTOs.

    /// <summary>Sum of all COMPLETED shifts' hours for today.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal WorkingHoursBase { get; set; }

    /// <summary>The last raw value we received from the API for hours.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal LastSeenWorkingHours { get; set; }

    /// <summary>Sum of all COMPLETED shifts' orders for today.</summary>
    public int OrdersBase { get; set; }

    /// <summary>The last raw value we received from the API for orders.</summary>
    public int LastSeenOrders { get; set; }
    // ─────────────────────────────────────────────────────────

    public int OrdersDayStart { get; set; }

    public int OrdersSnapshottedBeforeReset { get; set; }

    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}
