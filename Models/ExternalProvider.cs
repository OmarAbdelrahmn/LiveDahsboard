using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace LiveDahsboard.Models;


[Index(nameof(Username), IsUnique = true)]
[Index(nameof(ExpiresAt))]
public class ExternalProvider
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Username { get; set; } = null!;


    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsValid => DateTime.UtcNow >= StartsAt && DateTime.UtcNow <= ExpiresAt;
}