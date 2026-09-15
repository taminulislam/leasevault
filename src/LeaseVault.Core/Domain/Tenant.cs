using System.ComponentModel.DataAnnotations;

namespace LeaseVault.Core.Domain;

public class Tenant
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(200)]
    public string ContactEmail { get; set; } = string.Empty;

    [Phone, MaxLength(30)]
    public string? ContactPhone { get; set; }

    [MaxLength(60)]
    public string? Industry { get; set; }

    public ICollection<Lease> Leases { get; set; } = new List<Lease>();
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
