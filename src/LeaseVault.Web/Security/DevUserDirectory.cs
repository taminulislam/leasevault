using System.Security.Claims;
using LeaseVault.Core.Security;

namespace LeaseVault.Web.Security;

public sealed record DevUser(string UserName, string DisplayName, IReadOnlyList<string> Groups);

/// <summary>
/// Seeded users for the development cookie login (active only when AzureAd:ClientId is empty).
/// Every user signs in with the password <c>Passw0rd!</c>.
/// </summary>
public sealed class DevUserDirectory
{
    public const string Password = "Passw0rd!";

    public IReadOnlyList<DevUser> Users { get; } =
    [
        new("agent@leasevault.local", "Alex Agent", [GroupNames.Agents]),
        new("legal@leasevault.local", "Lena Legal", [GroupNames.Legal]),
        new("owner@leasevault.local", "Olivia Owner", [GroupNames.Owners]),
        new("admin@leasevault.local", "Adam Admin", [GroupNames.Admins, GroupNames.Agents, GroupNames.Legal, GroupNames.Owners])
    ];

    public DevUser? Validate(string? userName, string? password)
    {
        if (string.IsNullOrWhiteSpace(userName) || password != Password)
        {
            return null;
        }

        return Users.FirstOrDefault(u => string.Equals(u.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static ClaimsPrincipal CreatePrincipal(DevUser user, string authenticationScheme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserName),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.UserName),
            new("preferred_username", user.UserName),
            new(AppClaims.DisplayName, user.DisplayName)
        };
        claims.AddRange(user.Groups.Select(g => new Claim(AppClaims.Group, g)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationScheme, ClaimTypes.Name, ClaimTypes.Role));
    }
}
