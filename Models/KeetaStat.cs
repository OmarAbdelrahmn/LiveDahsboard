using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiveDahsboard.Models;

[Index(nameof(OrgId), nameof(Date))]
[Index(nameof(CourierId), nameof(OrgId), nameof(Date), IsUnique = true)]
public class KeetaStat
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string CourierId { get; set; } = null!;

    [Required, MaxLength(150)]
    public string CourierName { get; set; } = null!;

    [Required, MaxLength(50)]
    public string OrgId { get; set; } = null!;

    public DateOnly Date { get; set; }

    public int FinishedTasks { get; set; }
    public int DeliveringTasks { get; set; }
    public int CanceledTasks { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal OnlineHours { get; set; }

    public int StatusCode { get; set; }

    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}