using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using portfolio.Blog;

namespace portfolio.Pages.Admin;

[AllowAnonymous, EnableRateLimiting("blog-login")]
public sealed class LoginModel(BlogClient blog) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [BindProperty, Required, StringLength(1024)] public string Password { get; set; } = "";

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? LocalRedirect("/admin") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        try
        {
            var session = await blog.LoginAsync(Email.Trim(), Password);
            var id = await blog.GetUserIdAsync(session.AccessToken);
            if (id == null || session.ExpiresIn <= 0 || !await blog.IsAdminAsync(session.AccessToken))
            {
                await blog.LogoutAsync(session.AccessToken);
                ModelState.AddModelError("", "این حساب اجازهٔ مدیریت بلاگ را ندارد.");
                return Page();
            }
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, id.ToString()!),
                new Claim(BlogClient.TokenClaim, session.AccessToken)
            ], CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(session.ExpiresIn, 3600)),
                AllowRefresh = false
            });
            return LocalRedirect("/admin");
        }
        catch (BlogApiException e)
        {
            ModelState.AddModelError("", e.StatusCode == 429 ? "چند بار پشت سر هم برای ورود تلاش کرده‌ای. کمی صبر کن و دوباره امتحان کن." : e.StatusCode is 400 or 401 or 403 or 422 ? "ایمیل و رمز عبور را بررسی کن. ورود فقط برای حساب مدیر امکان‌پذیر است." : "در حال حاضر امکان ورود نیست. کمی بعد دوباره امتحان کن.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        { ModelState.AddModelError("", "در حال حاضر امکان ورود نیست. کمی بعد دوباره امتحان کن."); }
        // Password fields never render submitted values.
        Password = "";
        ModelState.Remove(nameof(Password));
        return Page();
    }
}
