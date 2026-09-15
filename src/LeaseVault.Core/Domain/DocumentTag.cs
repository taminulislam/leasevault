using System.ComponentModel.DataAnnotations;

namespace LeaseVault.Core.Domain;

public class DocumentTag
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document? Document { get; set; }

    [Required, MaxLength(60)]
    public string Value { get; set; } = string.Empty;
}
