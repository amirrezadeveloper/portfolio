using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using portfolio.Blog;

namespace portfolio.Pages.Admin;

public sealed class IndexModel(BlogClient blog) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "page")] public int? PageIndex { get; set; }
    public int PageNumber => Math.Clamp(PageIndex ?? 1, 1, 10000);
    public List<BlogPost> Posts { get; private set; } = [];
    public bool Failed { get; private set; }
    public async Task OnGetAsync()
    {
        try { Posts = await blog.ListAdminAsync(User.FindFirstValue(BlogClient.TokenClaim)!, PageNumber); }
        catch (Exception e) when (e is BlogApiException or HttpRequestException or TaskCanceledException)
        { Failed = true; Response.StatusCode = 503; }
    }
    public async Task<IActionResult> OnPostLogoutAsync()
    {
        try { await blog.LogoutAsync(User.FindFirstValue(BlogClient.TokenClaim)!); }
        catch (Exception e) when (e is BlogApiException or HttpRequestException or TaskCanceledException) { }
        await HttpContext.SignOutAsync();
        return LocalRedirect("/admin/login");
    }
}
