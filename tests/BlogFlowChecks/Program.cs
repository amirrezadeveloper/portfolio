using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using portfolio.Blog;

// Exercises the real ASP.NET pages against an isolated HTTP double, never the user's database.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
var productionProxy = args.Contains("--production-proxy");
var fakeBuilder = WebApplication.CreateBuilder(Array.Empty<string>());
fakeBuilder.Logging.ClearProviders();
fakeBuilder.WebHost.UseUrls("http://127.0.0.1:5192");
var fake = fakeBuilder.Build();
var posts = new List<BlogPost>();
var adminId = Guid.NewGuid();
var adminEnabled = true;
var unavailable = false;
fake.MapPost("/auth/v1/token", async (HttpRequest request) => {
    var input = await request.ReadFromJsonAsync<JsonElement>();
    return input.GetProperty("password").GetString() == "valid" ? Results.Json(new { access_token = input.GetProperty("email").GetString() == "admin@example.test" ? "admin-token" : "user-token", expires_in = 3600 }) : Results.StatusCode(400);
});
fake.MapGet("/auth/v1/user", (HttpRequest r) => Results.Json(new { id = r.Headers.Authorization == "Bearer admin-token" ? adminId : Guid.NewGuid() }));
fake.MapPost("/auth/v1/logout", () => Results.NoContent());
fake.MapPost("/rest/v1/rpc/is_blog_admin", (HttpRequest r) => Results.Json(adminEnabled && r.Headers.Authorization == "Bearer admin-token"));
fake.MapGet("/rest/v1/blog_posts", (HttpRequest r) => {
    if (unavailable) return Results.StatusCode(503);
    var filtered = posts.AsEnumerable();
    if (r.Headers.Authorization != "Bearer admin-token") filtered = filtered.Where(p => p.Status == "published");
    if (r.Query.TryGetValue("language", out var lang)) filtered = filtered.Where(p => "eq." + p.Language == lang);
    if (r.Query.TryGetValue("slug", out var slug)) filtered = filtered.Where(p => "eq." + p.Slug == slug);
    if (r.Query.TryGetValue("id", out var id)) filtered = filtered.Where(p => "eq." + p.Id == id);
    return Results.Json(filtered.ToList(), json);
});
fake.MapMethods("/rest/v1/blog_posts", ["POST", "PATCH"], async (HttpRequest r) => {
    if (!adminEnabled || r.Headers.Authorization != "Bearer admin-token") return Results.StatusCode(403);
    var post = await r.ReadFromJsonAsync<BlogPost>(json) ?? throw new Exception();
    var id = r.Query["id"].ToString().Replace("eq.", "");
    var existing = posts.FirstOrDefault(p => p.Id.ToString() == id);
    if (posts.Any(p => p.Slug == post.Slug && p.Language == post.Language && p != existing)) return Results.StatusCode(409);
    if (existing != null) posts.Remove(existing);
    post.Id = existing?.Id ?? Guid.NewGuid(); post.UpdatedAt = DateTimeOffset.UtcNow;
    posts.Add(post); return Results.Json(new[] { post }, json);
});
await fake.StartAsync();
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
start.ArgumentList.Add(Path.Combine(root, "bin/Debug/net10.0/portfolio.dll"));
start.Environment["ASPNETCORE_ENVIRONMENT"] = productionProxy ? "Production" : "Development";
if (productionProxy)
{
    start.Environment["DataProtection__KeyPath"] = Path.Combine(root, ".local", "production-proxy-test-keys");
    start.Environment["ReverseProxy__TrustPlatformHeaders"] = "true";
}
start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:5193";
start.Environment.Remove("PORT");
start.Environment["Supabase__Url"] = "http://127.0.0.1:5192";
start.Environment["Supabase__PublishableKey"] = "test-publishable";
using var server = Process.Start(start)!;
var logs = new List<string>();
server.OutputDataReceived += (_, e) => { if (e.Data != null) lock (logs) logs.Add(e.Data); };
server.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (logs) logs.Add(e.Data); };
server.BeginOutputReadLine(); server.BeginErrorReadLine();
using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new("http://127.0.0.1:5193") };
var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); checks++; Console.WriteLine("PASS: " + name); }
string Anti(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
async Task<HttpResponseMessage> Submit(string url, string html, Dictionary<string, string> values) { values["__RequestVerificationToken"] = Anti(html); return await client.PostAsync(url, new FormUrlEncodedContent(values)); }
Dictionary<string, string> Article(string slug, string action) => new() { ["Input.Title"] = "A test article", ["Input.Slug"] = slug, ["Input.Language"] = "en", ["Input.Excerpt"] = "An isolated fixture", ["Input.ContentMarkdown"] = "## Hello\n\n```csharp\nvar value = 42;\n```\n\n<script>alert(1)</script>\n\n[bad](javascript:alert)\n\n[docs](https://learn.microsoft.com)", ["Input.TagsText"] = ".NET, API", ["action"] = action };
try
{
    if (productionProxy) client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
    for (var i = 0; i < 40; i++) { try { await client.GetAsync("/admin/login"); break; } catch (HttpRequestException) { await Task.Delay(250); } }
    if (productionProxy)
    {
        var response = await client.GetAsync("/admin/login?ReturnUrl=%2Fadmin");
        var body = await response.Content.ReadAsStringAsync();
        Check(response.StatusCode == HttpStatusCode.OK && body.Contains("name=\"Email\""), "production login renders behind HTTPS-terminating proxy");
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Check(cookies.Any(c => c.Contains("secure", StringComparison.OrdinalIgnoreCase) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase)), "production antiforgery cookie remains Secure and HttpOnly");
        // Simulate the browser's HTTPS cookies forwarded over the private HTTP hop.
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(c => c.Split(';')[0])));
        var missing = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "admin@example.test", ["Password"] = "wrong" }));
        Check(missing.StatusCode == HttpStatusCode.BadRequest, "production login still rejects missing antiforgery token");
        var submitted = await Submit("/admin/login", body, new() { ["Email"] = "admin@example.test", ["Password"] = "wrong" });
        Check(submitted.StatusCode == HttpStatusCode.OK && (await submitted.Content.ReadAsStringAsync()).Contains("validation-summary-errors"), "production form validates antiforgery across proxy and handles login failure");
        var redirect = await client.GetAsync("/admin");
        Check(redirect.StatusCode == HttpStatusCode.Redirect && redirect.Headers.Location?.Scheme == "https", "production authentication redirect preserves HTTPS");
        Console.WriteLine($"{checks} production proxy checks passed.");
        return;
    }
    var blocked = await client.GetAsync("/admin/posts");
    Check(blocked.StatusCode == HttpStatusCode.Redirect && blocked.Headers.Location?.OriginalString.Contains("/admin/login") == true, "anonymous editor redirects to login");
    var loginHtml = await client.GetStringAsync("/admin/login");
    Check(!string.IsNullOrEmpty(Anti(loginHtml)), "login form has antiforgery token");
    var csrf = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "admin@example.test", ["Password"] = "valid" }));
    Check(csrf.StatusCode == HttpStatusCode.BadRequest, "login rejects missing antiforgery token");
    var wrong = await Submit("/admin/login", loginHtml, new() { ["Email"] = "admin@example.test", ["Password"] = "wrong" });
    Check(wrong.StatusCode == HttpStatusCode.OK && !(await wrong.Content.ReadAsStringAsync()).Contains("value=\"wrong\""), "wrong password rejected and never redisplayed");
    var ordinary = await Submit("/admin/login", loginHtml, new() { ["Email"] = "user@example.test", ["Password"] = "valid" });
    Check(ordinary.StatusCode == HttpStatusCode.OK && !ordinary.Headers.TryGetValues("Set-Cookie", out _), "non-admin account cannot receive an auth cookie");
    var login = await Submit("/admin/login", loginHtml, new() { ["Email"] = "admin@example.test", ["Password"] = "valid" });
    Check(login.StatusCode == HttpStatusCode.Redirect && login.Headers.Location?.OriginalString == "/admin", "admin can sign in");
    Check(login.Headers.GetValues("Set-Cookie").All(c => !c.Contains("admin-token")), "access token never appears in plaintext cookie");
    var editor = await client.GetStringAsync("/admin/posts");
    var missingToken = await client.PostAsync("/admin/posts?handler=Save", new FormUrlEncodedContent(Article("test-note", "publish")));
    Check(missingToken.StatusCode == HttpStatusCode.BadRequest && posts.Count == 0, "editor write rejects missing antiforgery");
    var draft = await Submit("/admin/posts?handler=Save", editor, Article("test-note", "draft"));
    Check(draft.StatusCode == HttpStatusCode.Redirect && posts.Single().Status == "draft", "draft saved through actual page handler");
    Check((await client.GetAsync("/blog/test-note")).StatusCode == HttpStatusCode.NotFound, "public route hides drafts even with admin cookie");
    var id = posts.Single().Id;
    var edit = await client.GetStringAsync("/admin/posts/" + id);
    var preview = await Submit("/admin/posts/" + id + "?handler=Preview", edit, Article("test-note", "publish"));
    Check(preview.StatusCode == HttpStatusCode.OK && posts.Single().Status == "draft", "preview does not publish or modify stored data");
    var publish = await Submit("/admin/posts/" + id + "?handler=Save", edit, Article("test-note", "publish"));
    Check(publish.StatusCode == HttpStatusCode.Redirect && posts.Single().Status == "published" && posts.Single().PublishedAt != null, "publish sets status and date");
    var articleResponse = await client.GetAsync("/blog/test-note");
    var article = await articleResponse.Content.ReadAsStringAsync();
    Check(articleResponse.IsSuccessStatusCode && article.Contains("<h2>Hello</h2>") && article.Contains("language-csharp"), "published article renders markdown and code");
    Check(!article.Contains("<script>alert") && !article.Contains("href=\"javascript:"), "article HTML and executable links disabled");
    Check((await client.GetAsync("/fa/blog/test-note")).StatusCode == HttpStatusCode.NotFound, "article language isolation");
    Check((await client.GetStringAsync("/blog")).Contains("A test article"), "published article appears in listing");
    var invalid = Article("INVALID slug", "publish");
    Check((await Submit("/admin/posts?handler=Save", editor, invalid)).StatusCode == HttpStatusCode.OK && posts.Count == 1, "invalid slug does not mutate data");
    var duplicate = await Submit("/admin/posts?handler=Save", editor, Article("test-note", "publish"));
    Check(duplicate.StatusCode == HttpStatusCode.OK && posts.Count == 1, "duplicate slug handled without losing submitted form");
    var archive = await Submit("/admin/posts/" + id + "?handler=Save", edit, Article("test-note", "archive"));
    Check(archive.StatusCode == HttpStatusCode.Redirect && (await client.GetAsync("/blog/test-note")).StatusCode == HttpStatusCode.NotFound, "archive removes article from public view");
    unavailable = true;
    Check((await client.GetAsync("/blog")).StatusCode == HttpStatusCode.ServiceUnavailable, "upstream failure renders 503 instead of empty-success page");
    unavailable = false;
    adminEnabled = false;
    Check((await client.GetAsync("/admin/posts")).StatusCode == HttpStatusCode.Redirect, "revoked admin membership rejects existing session");
    var html = ArticleMarkdown.Render("[one](data:text/html,x) [two](javascript:alert) <iframe src=x></iframe> [ok](https://example.com)");
    Check(!html.Contains("href=\"data:") && !html.Contains("href=\"javascript:") && !html.Contains("<iframe") && html.Contains("href=\"https://example.com\""), "markdown URI allowlist preserves valid web links");
    var auto = ArticleMarkdown.Render("<javascript:alert> <data:text/html,x> <https://example.com>");
    Check(!auto.Contains("href=\"javascript:") && !auto.Contains("href=\"data:") && auto.Contains("href=\"https://example.com\""), "markdown autolinks reject executable URI schemes");
    Console.WriteLine($"{checks} checks passed.");
}
catch (Exception error) { lock (logs) Console.Error.WriteLine(string.Join(Environment.NewLine, logs.TakeLast(35))); Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
finally { if (!server.HasExited) server.Kill(true); await fake.StopAsync(); }
