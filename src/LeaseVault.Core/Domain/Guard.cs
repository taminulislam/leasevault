namespace LeaseVault.Core.Domain;

internal static class Guard
{
    public static void NotEmpty(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", paramName);
        }
    }
}
