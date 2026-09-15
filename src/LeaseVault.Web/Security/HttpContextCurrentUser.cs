using System.Security.Claims;
using LeaseVault.Core.Abstractions;

namespace LeaseVault.Web.Security;

/// <summary>
/// Resolves the caller from the current HTTP request. Background services run without a request
/// and are reported as the "system" actor.
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    public const string SystemUser = "system";

    private readonly ClaimsPrincipal? _principal;

    public HttpContextCurrentUser(IHttpContextAccessor accessor)
    {
        _principal = accessor.HttpContext?.User;
    }

    public bool IsAuthenticated => _principal?.Identity?.IsAuthenticated == true;

    public string Name => IsAuthenticated
        ? _principal!.FindFirst("preferred_username")?.Value
          ?? _principal.FindFirst(ClaimTypes.Upn)?.Value
          ?? _principal.FindFirst(ClaimTypes.Email)?.Value
          ?? _principal.Identity!.Name
          ?? SystemUser
        : SystemUser;

    public string DisplayName => IsAuthenticated
        ? _principal!.FindFirst(AppClaims.DisplayName)?.Value ?? _principal.FindFirst("name")?.Value ?? Name
        : SystemUser;

    public IReadOnlyCollection<string> Groups => IsAuthenticated
        ? _principal!.FindAll(AppClaims.Group).Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        : [];
}
