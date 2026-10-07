using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace portfolio.Blog;

public sealed class BlogCookieEvents(BlogClient blog) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/admin")) return;
        var token = context.Principal?.FindFirstValue(BlogClient.TokenClaim);
        try
        {
            var id = token == null ? null : await blog.GetUserIdAsync(token);
            if (id != null && id.ToString() == context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) && await blog.IsAdminAsync(token!)) return;
        }
        catch (Exception e) when (e is BlogApiException or HttpRequestException or TaskCanceledException) { }
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync();
    }
}
