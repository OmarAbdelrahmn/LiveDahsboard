using Microsoft.EntityFrameworkCore;

namespace LiveDahsboard.Models;

[Index(nameof(CompanyId), nameof(Date))]
[Index(nameof(RiderId), nameof(CompanyId), nameof(ActiveShiftStartedAt), IsUnique = true)]
public class RiderShiftStat
{
    public int Id { get; set; }

    public string RiderId { get; set; } = string.Empty;
    public string RiderName { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;

    // ── Primary shift identity ─────────────────────────────────────────
    public DateTime ActiveShiftStartedAt { get; set; }   // UTC

    // ── Raw snapshots — no accumulation, no heuristics ────────────────
    public int Orders { get; set; }
    public decimal WorkingHours { get; set; }
    public decimal Wallet { get; set; }

    // ── Metadata ──────────────────────────────────────────────────────
    public DateOnly Date { get; set; }   // kept for easy day-level queries
    public DateTime LastUpdatedAt { get; set; }
}