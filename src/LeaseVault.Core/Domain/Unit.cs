using System.ComponentModel.DataAnnotations;

namespace LeaseVault.Core.Domain;

public class Unit
{
    public int Id { get; set; }

    public int PropertyId { get; set; }
    public Property? Property { get; set; }

    [Required, MaxLength(20)]
    public string UnitNumber { get; set; } = string.Empty;

    public int? Floor { get; set; }

    [Range(0, 1_000_000)]
    public int SquareFeet { get; set; }

    public ICollection<Lease> Leases { get; set; } = new List<Lease>();
}
