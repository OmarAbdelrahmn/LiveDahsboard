using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiveDahsboard.Models;

/// <summary>
/// One raw snapshot saved every time the front-end pushes data.
/// No aggregation, no shift tracking — just a timestamped log.
/// The service layer derives working hours / orders at read-time
/// by detecting session resets (value drops) and computing last−first
/// per session.
/// </summary>
[Index(nameof(CompanyId), nameof(Date))]
[Index(nameof(RiderId), nameof(CompanyId), nameof(Date))]
[Index(nameof(RecordedAtUtc))]
public class RiderSnapshot
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string RiderId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string RiderName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string CompanyId { get; set; } = string.Empty;

    /// <summary>Local (Saudi) calendar date — used for day-level queries.</summary>
    public DateOnly Date { get; set; }

    /// <summary>UTC instant this snapshot was persisted.</summary>
    public DateTime RecordedAtUtc { get; set; }

    /// <summary>
    /// Raw cumulative orders from the API for the rider's current shift.
    /// Resets to a low value when a new shift starts.
    /// </summary>
    public int Orders { get; set; }

    /// <summary>
    /// Raw cumulative working hours from the API for the rider's current shift.
    /// Resets to a low value when a new shift starts.
    /// </summary>
    [Column(TypeName = "decimal(10,4)")]
    public decimal WorkingHours { get; set; }

    /// <summary>Latest wallet balance — not cumulative, just a snapshot.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Wallet { get; set; }
}