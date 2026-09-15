using LeaseVault.Core.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

namespace LeaseVault.Web.Security;

/// <summary>Claim types the application adds on top of the identity provider's claims.</summary>
public static class AppClaims
{
    /// <summary>Application security group name (Agents / Legal / Owners / Admins).</summary>
    public const string Group = "lv:group";

    public const string DisplayName = "lv:displayname";
}

/// <summary>Tells the UI which sign-in experience is active.</summary>
public sealed record AuthenticationMode(bool UsesEntraId)
{
    public string SignInPath => UsesEntraId ? "/MicrosoftIdentity/Account/SignIn" : "/Account/Login";
    public string SignOutPath => UsesEntraId ? "/MicrosoftIdentity/Account/SignOut" : "/Account/Logout";
}

public static class AuthenticationSetup
{
    /// <summary>
    /// Entra ID (Microsoft.Identity.Web) when <c>AzureAd:ClientId</c> is configured; otherwise a
    /// development cookie login backed by <see cref="DevUserDirectory"/>. Group-based policies
    /// are identical in both modes because both end up with <see cref="AppClaims.Group"/> claims.
    /// </summary>
    public static IServiceCollection AddLeaseVaultAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var azureAd = configuration.GetSection("AzureAd");
        var usesEntra = !string.IsNullOrWhiteSpace(azureAd["ClientId"]);
        services.AddSingleton(new AuthenticationMode(usesEntra));

        if (usesEntra)
        {
            services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(azureAd);
            services.AddControllersWithViews().AddMicrosoftIdentityUI();
            services.AddSingleton<Microsoft.AspNetCore.Authentication.IClaimsTransformation, EntraGroupClaimsTransformation>();
        }
        else
        {
            services.AddSingleton<DevUserDirectory>();
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Account/Login";
                    options.LogoutPath = "/Account/Logout";
                    options.AccessDeniedPath = "/Account/AccessDenied";
                    options.Cookie.Name = "LeaseVault.Dev";
                    options.ExpireTimeSpan = TimeSpan.FromHours(8);
                    options.SlidingExpiration = true;
                });
        }

        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.Agents, p => p.RequireClaim(AppClaims.Group, GroupNames.Agents, GroupNames.Admins));
            options.AddPolicy(Policies.Legal, p => p.RequireClaim(AppClaims.Group, GroupNames.Legal, GroupNames.Admins));
            options.AddPolicy(Policies.Owners, p => p.RequireClaim(AppClaims.Group, GroupNames.Owners, GroupNames.Admins));
            options.AddPolicy(Policies.Admins, p => p.RequireClaim(AppClaims.Group, GroupNames.Admins));
            options.AddPolicy(Policies.Approvers, p => p.RequireClaim(AppClaims.Group, GroupNames.Agents, GroupNames.Legal, GroupNames.Owners, GroupNames.Admins));
            options.AddPolicy(Policies.Contributors, p => p.RequireClaim(AppClaims.Group, GroupNames.Agents, GroupNames.Admins));
        });

        return services;
    }
}
