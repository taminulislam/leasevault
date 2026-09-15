using System.Security.Claims;
using LeaseVault.Core.Security;
using Microsoft.AspNetCore.Authentication;

namespace LeaseVault.Web.Security;

/// <summary>
/// Maps Entra ID group object ids (the "groups" claim emitted when the app registration has
/// the groups optional claim) onto application group names using <c>AzureAd:Groups:*</c>.
/// </summary>
public sealed class EntraGroupClaimsTransformation : IClaimsTransformation
{
    private readonly IReadOnlyDictionary<string, string> _groupIdToName;

    public EntraGroupClaimsTransformation(IConfiguration configuration)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in GroupNames.All)
        {
            var id = configuration[$"AzureAd:Groups:{group}"];
            if (!string.IsNullOrWhiteSpace(id))
            {
                map[id] = group;
            }
        }

        _groupIdToName = map;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated || identity.HasClaim(c => c.Type == AppClaims.Group))
        {
            return Task.FromResult(principal);
        }

        foreach (var claim in principal.FindAll("groups").Concat(principal.FindAll(ClaimTypes.Role)))
        {
            if (_groupIdToName.TryGetValue(claim.Value, out var name) || GroupNames.All.Contains(claim.Value, StringComparer.OrdinalIgnoreCase))
            {
                identity.AddClaim(new Claim(AppClaims.Group, name ?? claim.Value));
            }
        }

        return Task.FromResult(principal);
    }
}
