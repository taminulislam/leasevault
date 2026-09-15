using System.ComponentModel.DataAnnotations;

namespace LeaseVault.Core.Domain;

public class Property
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string AddressLine1 { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string City { get; set; } = string.Empty;

    [Required, MaxLength(2)]
    public string State { get; set; } = "IL";

    [Required, MaxLength(10)]
    public string PostalCode { get; set; } = string.Empty;

    [MaxLength(60)]
    public string? PropertyType { get; set; }

    public ICollection<Unit> Units { get; set; } = new List<Unit>();
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
