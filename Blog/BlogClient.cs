using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace portfolio.Blog;

// Every request carries its own credentials; no shared mutable authentication state.
public sealed class BlogClient(HttpClient http, IOptions<SupabaseOptions> options)
{
    public const string TokenClaim = "supabase_access_token";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public bool IsConfigured => Uri.TryCreate(options.Value.Url, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(options.Value.PublishableKey);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token = null, object? body = null, bool representation = false)
    {
        if (!IsConfigured) throw new BlogApiException(503);
        using var request = new HttpRequestMessage(method, options.Value.Url.TrimEnd('/') + path);
        request.Headers.Add("apikey", options.Value.PublishableKey);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) request.Content = JsonContent.Create(body, options: Json);
        if (representation) request.Headers.Add("Prefer", "return=representation");
        var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new BlogApiException(status);
        }
        return response;
    }

    public async Task<List<BlogPost>> ListPublishedAsync(string language, int page = 1)
    {
        var now = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        using var response = await SendAsync(HttpMethod.Get, $"/rest/v1/blog_posts?select=*&language=eq.{language}&status=eq.published&published_at=lte.{now}&order=published_at.desc,id.desc&limit=13&offset={(Math.Max(page, 1) - 1) * 12}");
        return await response.Content.ReadFromJsonAsync<List<BlogPost>>(Json) ?? [];
    }

    public async Task<BlogPost?> GetPublishedAsync(string language, string slug)
    {
        var now = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        using var response = await SendAsync(HttpMethod.Get, $"/rest/v1/blog_posts?select=*&language=eq.{language}&slug=eq.{Uri.EscapeDataString(slug)}&status=eq.published&published_at=lte.{now}&limit=1");
        return (await response.Content.ReadFromJsonAsync<List<BlogPost>>(Json))?.FirstOrDefault();
    }

    public async Task<List<BlogPost>> ListAdminAsync(string token, int page = 1)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/rest/v1/blog_posts?select=*&order=updated_at.desc,id.desc&limit=21&offset={(Math.Max(page, 1) - 1) * 20}", token);
        return await response.Content.ReadFromJsonAsync<List<BlogPost>>(Json) ?? [];
    }

    public async Task<BlogPost?> GetAdminAsync(Guid id, string token)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/rest/v1/blog_posts?select=*&id=eq.{id}&limit=1", token);
        return (await response.Content.ReadFromJsonAsync<List<BlogPost>>(Json))?.FirstOrDefault();
    }

    public async Task<BlogPost> SaveAsync(BlogPost post, string token)
    {
        // Never accept author_id, timestamps, or an arbitrary status from submitted forms.
        var body = new { post.Title, post.Slug, post.Language, post.Excerpt, post.ContentMarkdown, post.Tags, post.Status, post.PublishedAt };
        using var response = await SendAsync(post.Id == Guid.Empty ? HttpMethod.Post : HttpMethod.Patch,
            post.Id == Guid.Empty ? "/rest/v1/blog_posts" : $"/rest/v1/blog_posts?id=eq.{post.Id}", token, body, true);
        return (await response.Content.ReadFromJsonAsync<List<BlogPost>>(Json))?.SingleOrDefault() ?? throw new BlogApiException(403);
    }

    public async Task<LoginSession> LoginAsync(string email, string password)
    {
        using var response = await SendAsync(HttpMethod.Post, "/auth/v1/token?grant_type=password", body: new { email, password });
        return await response.Content.ReadFromJsonAsync<LoginSession>(Json) ?? throw new BlogApiException(401);
    }

    public async Task<Guid?> GetUserIdAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Get, "/auth/v1/user", token);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var userId) ? userId : null;
    }

    public async Task<bool> IsAdminAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Post, "/rest/v1/rpc/is_blog_admin", token, new { });
        return await response.Content.ReadFromJsonAsync<bool>();
    }

    public async Task LogoutAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Post, "/auth/v1/logout?scope=local", token);
    }

    public sealed class LoginSession
    {
        public string AccessToken { get; set; } = "";
        public int ExpiresIn { get; set; }
    }
}
