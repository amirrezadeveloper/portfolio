using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using portfolio.Blog;

namespace portfolio.Pages.Admin;

[RequestSizeLimit(600000)]
public sealed class EditModel(BlogClient blog) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public ArticleInput Input { get; set; } = new();
    public string? PreviewHtml { get; private set; }
    private string Token => User.FindFirstValue(BlogClient.TokenClaim)!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id == null) return Page();
        try
        {
            var post = await blog.GetAdminAsync(Id.Value, Token);
            if (post == null) return NotFound();
            Input = new() { Title = post.Title, Slug = post.Slug, Language = post.Language, Excerpt = post.Excerpt, ContentMarkdown = post.ContentMarkdown, TagsText = string.Join(", ", post.Tags) };
        }
        catch (Exception e) when (e is BlogApiException or HttpRequestException or TaskCanceledException)
        { ModelState.AddModelError("", "مقاله بارگذاری نشد. صفحه را دوباره باز کن."); Response.StatusCode = 503; }
        return Page();
    }

    public IActionResult OnPostPreview()
    {
        if (ModelState.IsValid) PreviewHtml = ArticleMarkdown.Render(Input.ContentMarkdown);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(string action)
    {
        Input.Title = Input.Title?.Trim() ?? "";
        Input.Slug = Input.Slug?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(Input.Title) || string.IsNullOrWhiteSpace(Input.ContentMarkdown)) ModelState.AddModelError("", "عنوان و متن مقاله را بنویس.");
        if (action is not ("publish" or "draft" or "archive")) ModelState.AddModelError("", "عملیات نامعتبر است.");
        var tags = (Input.TagsText ?? "").Split([',', '،'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tags.Length > 20 || tags.Any(t => t.Length > 50)) ModelState.AddModelError("", "حداکثر ۲۰ برچسب و برای هر برچسب حداکثر ۵۰ کاراکتر مجاز است.");
        if (!ModelState.IsValid) return Page();
        try
        {
            var existing = Id == null ? null : await blog.GetAdminAsync(Id.Value, Token);
            if (Id != null && existing == null) return NotFound();
            var post = new BlogPost { Id = Id ?? Guid.Empty, Title = Input.Title, Slug = Input.Slug, Language = Input.Language, Excerpt = Input.Excerpt ?? "", ContentMarkdown = Input.ContentMarkdown!, Tags = tags,
                Status = action == "publish" ? "published" : action == "archive" ? "archived" : "draft",
                PublishedAt = action == "publish" ? existing?.PublishedAt ?? DateTimeOffset.UtcNow : existing?.PublishedAt };
            var saved = await blog.SaveAsync(post, Token);
            return LocalRedirect("/admin?saved=1&draft=" + (Id?.ToString() ?? "new"));
        }
        catch (BlogApiException e)
        { ModelState.AddModelError("", e.StatusCode == 409 ? "این آدرس قبلاً برای همین زبان استفاده شده؛ آدرس دیگری انتخاب کن." : e.StatusCode is 401 or 403 ? "برای ذخیرهٔ مقاله باید دوباره وارد حساب مدیر شوی. نسخهٔ موقت نوشته در مرورگر می‌ماند." : "مقاله ذخیره نشد. برای اطمینان، متن را کپی کن و دوباره امتحان کن."); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        { ModelState.AddModelError("", "ارتباط با سرور برقرار نشد. برای اطمینان، متن را کپی کن و دوباره امتحان کن."); }
        return Page();
    }

    public sealed class ArticleInput
    {
        [Required(ErrorMessage = "عنوان را بنویس."), StringLength(250)] public string Title { get; set; } = "";
        [Required(ErrorMessage = "آدرس مقاله را بنویس."), StringLength(160), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "آدرس باید از حروف کوچک انگلیسی، عدد و خط تیره ساخته شود.")] public string Slug { get; set; } = "";
        [Required, RegularExpression("^(fa|en)$")] public string Language { get; set; } = "fa";
        [StringLength(2000)] public string? Excerpt { get; set; } = "";
        [Required(ErrorMessage = "متن مقاله را بنویس."), StringLength(100000)] public string ContentMarkdown { get; set; } = "";
        [StringLength(1000)] public string? TagsText { get; set; } = "";
    }
}
