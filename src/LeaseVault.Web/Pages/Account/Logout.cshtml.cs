using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaseVault.Web.Pages.Account;

[AllowAnonymous]
public class LogoutModel : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return RedirectToPage("/Account/Login");
    }

    public Task<IActionResult> OnPostAsync() => OnGetAsync();
}
