using System.ComponentModel.DataAnnotations;
using LeaseVault.Core.Abstractions;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaseVault.Web.Pages.Account;

/// <summary>Development cookie sign-in. Not registered when Entra ID is configured.</summary>
[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly DevUserDirectory? _users;
    private readonly AuthenticationMode _mode;
    private readonly LeaseVaultDbContext _db;
    private readonly IClock _clock;

    public LoginModel(AuthenticationMode mode, LeaseVaultDbContext db, IClock clock, DevUserDirectory? users = null)
    {
        _mode = mode;
        _db = db;
        _clock = clock;
        _users = users;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public IReadOnlyList<DevUser> SeededUsers => _users?.Users ?? [];

    public string Password => DevUserDirectory.Password;

    public class InputModel
    {
        [Required, EmailAddress]
        public string UserName { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (_mode.UsesEntraId)
        {
            return Redirect(_mode.SignInPath);
        }

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (_users is null)
        {
            return Redirect(_mode.SignInPath);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = _users.Validate(Input.UserName, Input.Password);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid user name or password.");
            return Page();
        }

        var principal = DevUserDirectory.CreatePrincipal(user, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });

        _db.AuditEntries.Add(Core.Domain.AuditEntry.Create(user.UserName, AuditActions.SignedIn, "User", user.UserName, "Dev cookie sign-in", _clock.UtcNow));
        await _db.SaveChangesAsync();

        return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
    }
}
