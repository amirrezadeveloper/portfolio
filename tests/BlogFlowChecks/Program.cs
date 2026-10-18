using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using portfolio.Blog;

// Exercises the real ASP.NET pages against an isolated HTTP double, never the user's database.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
var sharedKeys = args.Contains("--shared-keys");
var productionProxy = sharedKeys || args.Contains("--production-proxy");
var fakeBuilder = WebApplication.CreateBuilder(Array.Empty<string>());
fakeBuilder.Logging.ClearProviders();
fakeBuilder.WebHost.UseUrls("http://127.0.0.1:5192");
var fake = fakeBuilder.Build();
var posts = new List<BlogPost>();
var adminId = Guid.NewGuid();
var adminEnabled = true;
var unavailable = false;
var metadataUnavailable = false;
fake.MapPost("/auth/v1/token", async (HttpRequest request) => {
    var input = await request.ReadFromJsonAsync<JsonElement>();
    return input.GetProperty("password").GetString() == "valid" ? Results.Json(new { access_token = input.GetProperty("email").GetString() == "admin@example.test" ? "admin-token" : "user-token", expires_in = 3600 }) : Results.StatusCode(400);
});
fake.MapGet("/auth/v1/user", (HttpRequest r) => Results.Json(new { id = r.Headers.Authorization == "Bearer admin-token" ? adminId : Guid.NewGuid() }));
fake.MapPost("/auth/v1/logout", () => Results.NoContent());
fake.MapPost("/rest/v1/rpc/is_blog_admin", (HttpRequest r) => Results.Json(adminEnabled && r.Headers.Authorization == "Bearer admin-token"));
fake.MapGet("/rest/v1/blog_posts", (HttpRequest r) => {
    if (unavailable || metadataUnavailable && r.Query["select"] != "*") return Results.StatusCode(503);
    var filtered = posts.AsEnumerable();
    if (r.Headers.Authorization != "Bearer admin-token") filtered = filtered.Where(p => p.Status == "published");
    if (r.Query.TryGetValue("language", out var lang)) filtered = filtered.Where(p => "eq." + p.Language == lang);
    if (r.Query.TryGetValue("slug", out var slug)) filtered = filtered.Where(p => "eq." + p.Slug == slug);
    if (r.Query.TryGetValue("id", out var id)) filtered = filtered.Where(p => "eq." + p.Id == id);
    if (r.Query.TryGetValue("status", out var state)) filtered = filtered.Where(p => "eq." + p.Status == state);
    if (r.Query.TryGetValue("published_at", out var published))
    {
        var cutoff = DateTimeOffset.Parse(published.ToString()[4..], System.Globalization.CultureInfo.InvariantCulture);
        filtered = filtered.Where(p => p.PublishedAt != null && p.PublishedAt <= cutoff);
    }
    filtered = filtered.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id);
    if (int.TryParse(r.Query["offset"], out var offset)) filtered = filtered.Skip(offset);
    if (int.TryParse(r.Query["limit"], out var limit)) filtered = filtered.Take(limit);
    if (r.Query["select"] != "*") return Results.Json(filtered.Select(p => new
    { p.Id, p.Slug, p.Language, p.Title, p.Excerpt, p.Tags, p.Status, p.PublishedAt, p.UpdatedAt, p.CreatedAt }).ToList(), json);
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
    start.Environment["DataProtection__KeyPath"] = Path.Combine(root, ".local", sharedKeys ? "shared-key-test-" + Guid.NewGuid().ToString("N") : "production-proxy-test-keys");
    start.Environment["ReverseProxy__TrustPlatformHeaders"] = "true";
}
start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:5193";
start.Environment.Remove("PORT");
start.Environment["Supabase__Url"] = "http://127.0.0.1:5192";
start.Environment["Supabase__PublishableKey"] = "test-publishable";
start.Environment["Seo__SiteUrl"] = "https://portfolio.example";
start.Environment["Seo__GoogleSiteVerification"] = "verification-fixture";
if (sharedKeys)
{
    start.Environment["DataProtection__ReadOnlyKeys"] = "true";
    var initialize = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    initialize.ArgumentList.Add(start.ArgumentList[0]);
    initialize.ArgumentList.Add("--initialize-data-protection");
    initialize.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
    initialize.Environment["DataProtection__KeyPath"] = start.Environment["DataProtection__KeyPath"];
    using var keyInitializer = Process.Start(initialize)!;
    var initOutput = keyInitializer.StandardOutput.ReadToEndAsync();
    var initErrors = keyInitializer.StandardError.ReadToEndAsync();
    await keyInitializer.WaitForExitAsync();
    await Task.WhenAll(initOutput, initErrors);
    if (keyInitializer.ExitCode != 0) throw new Exception("Private image key initialization failed.");
}
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
        var stale = await Submit("/admin/login", "<input name=\"__RequestVerificationToken\" value=\"stale-token\">", new() { ["Email"] = "admin@example.test", ["Password"] = "valid" });
        Check(stale.StatusCode == HttpStatusCode.Redirect && stale.Headers.Location?.OriginalString == "/admin/login?expired=1", "expired form is rejected with a fresh-login redirect");
        var refreshed = await client.GetStringAsync("/admin/login?expired=1");
        Check(WebUtility.HtmlDecode(refreshed).Contains("اعتبار فرم قبلی تمام شده"), "expired form recovery explains why another sign-in is needed");
        if (sharedKeys)
        {
            start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:5194";
            using var second = Process.Start(start)!;
            second.BeginOutputReadLine(); second.BeginErrorReadLine();
            using var other = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new("http://127.0.0.1:5194") };
            other.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            var cookieHeader = string.Join("; ", cookies.Select(c => c.Split(';')[0]));
            other.DefaultRequestHeaders.Add("Cookie", cookieHeader);
            try
            {
                for (var i = 0; i < 40; i++) { try { await other.GetAsync("/admin/login"); break; } catch (HttpRequestException) { await Task.Delay(250); } }
                var across = await other.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "admin@example.test", ["Password"] = "wrong", ["__RequestVerificationToken"] = Anti(body) }));
                Check(across.StatusCode == HttpStatusCode.OK, "form from one production replica validates on another replica");
                var signedIn = await other.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "admin@example.test", ["Password"] = "valid", ["__RequestVerificationToken"] = Anti(body) }));
                Check(signedIn.StatusCode == HttpStatusCode.Redirect && signedIn.Headers.Location?.OriginalString == "/admin", "production admin fixture signs in on a different replica");
                var authenticatedCookies = cookieHeader + "; " + string.Join("; ", signedIn.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
                client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", authenticatedCookies);
                Check((await client.GetAsync("/admin")).StatusCode == HttpStatusCode.OK, "authentication cookie works on the original replica");
                second.Kill(true); await second.WaitForExitAsync();
                using var restarted = Process.Start(start)!;
                restarted.BeginOutputReadLine(); restarted.BeginErrorReadLine();
                other.DefaultRequestHeaders.Remove("Cookie"); other.DefaultRequestHeaders.Add("Cookie", authenticatedCookies);
                try
                {
                    HttpResponseMessage? afterRestart = null;
                    for (var i = 0; i < 40; i++) { try { afterRestart = await other.GetAsync("/admin"); break; } catch (HttpRequestException) { await Task.Delay(250); } }
                    Check(afterRestart?.StatusCode == HttpStatusCode.OK, "authentication cookie survives a production replica cold start");
                }
                finally { if (!restarted.HasExited) restarted.Kill(true); }
            }
            finally { if (!second.HasExited) second.Kill(true); }
        }
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
    var publishedPost = posts.Single();
    publishedPost.UpdatedAt = publishedPost.PublishedAt!.Value.AddDays(1);
    var translated = new BlogPost { Id = Guid.NewGuid(), Slug = "test-note", Language = "fa", Title = "مقالهٔ آزمایشی", Excerpt = "متن فارسی", ContentMarkdown = "## فارسی", Status = "published", PublishedAt = DateTimeOffset.UtcNow.AddDays(-2), UpdatedAt = DateTimeOffset.UtcNow, Tags = ["API"] };
    var related = new BlogPost { Id = Guid.NewGuid(), Slug = "related-note", Language = "en", Title = "Related architecture", Excerpt = "Related", Status = "published", PublishedAt = DateTimeOffset.UtcNow.AddDays(-3), UpdatedAt = DateTimeOffset.UtcNow, Tags = ["API"] };
    var future = new BlogPost { Id = Guid.NewGuid(), Slug = "future-note", Language = "en", Title = "Scheduled", Status = "published", PublishedAt = DateTimeOffset.UtcNow.AddDays(2) };
    var privateDraft = new BlogPost { Id = Guid.NewGuid(), Slug = "private-draft", Language = "fa", Title = "Private", Status = "draft" };
    posts.AddRange([translated, related, future, privateDraft]);
    var sitemap = XDocument.Parse(await client.GetStringAsync("/sitemap.xml"));
    XNamespace sitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";
    XNamespace xhtml = "http://www.w3.org/1999/xhtml";
    var urls = sitemap.Root!.Elements(sitemapNs + "url").ToArray();
    Check(urls.Length == 7 && !sitemap.ToString().Contains("future-note") && !sitemap.ToString().Contains("private-draft"), "sitemap includes published pages and excludes drafts and scheduled posts");
    var entry = urls.Single(x => x.Element(sitemapNs + "loc")!.Value == "https://portfolio.example/blog/test-note");
    Check(entry.Elements(xhtml + "link").Count() == 3 && entry.Element(sitemapNs + "lastmod") != null, "sitemap carries reciprocal translations and actual modification dates");
    article = await client.GetStringAsync("/blog/test-note");
    Check(article.Contains("hreflang=\"fa\" href=\"https://portfolio.example/fa/blog/test-note\"") && article.Contains("href=\"/fa/blog/test-note\""), "article and language selector link to the published translation");
    Check(article.Contains("href=\"/blog/related-note\"") && !article.Contains("href=\"/blog/future-note\""), "related articles are same-language published links");
    var schemaText = Regex.Matches(article, "<script\\b([^>]*)>(.*?)</script>", RegexOptions.Singleline)
        .Single(m => WebUtility.HtmlDecode(m.Groups[1].Value).Contains("application/ld+json")).Groups[2].Value;
    using var schema = JsonDocument.Parse(schemaText);
    var graph = schema.RootElement.GetProperty("@graph");
    Check(graph[0].GetProperty("@type").GetString() == "BlogPosting" && graph[0].GetProperty("author").GetProperty("name").GetString() == "Amirreza Ghahremani", "article JSON-LD identifies the article and author");
    Check(graph[1].GetProperty("itemListElement")[0].GetProperty("@type").GetString() == "ListItem" && article.Contains("rel=\"author\"") && article.Contains("Updated:"), "breadcrumbs and visible author/update metadata are rendered");
    var missingTranslation = await client.GetStringAsync("/blog/related-note");
    Check(!missingTranslation.Contains("hreflang=\"fa\""), "untranslated articles do not advertise nonexistent alternate pages");
    metadataUnavailable = true;
    Check((await client.GetAsync("/blog/test-note")).StatusCode == HttpStatusCode.OK && (await client.GetAsync("/sitemap.xml")).StatusCode == HttpStatusCode.ServiceUnavailable, "metadata outage preserves article readability and returns retryable sitemap failure");
    metadataUnavailable = false;
    var extra = Enumerable.Range(0, 205).Select(i => new BlogPost { Id = Guid.NewGuid(), Slug = "page-item-" + i, Language = "en", Title = "Paging " + i, Status = "published", PublishedAt = DateTimeOffset.UtcNow.AddDays(-10).AddMinutes(-i) }).ToArray();
    posts.AddRange(extra);
    Check(XDocument.Parse(await client.GetStringAsync("/sitemap.xml")).Root!.Elements(sitemapNs + "url").Count() == 212, "sitemap fetches metadata beyond the first API batch");
    var pageTwo = await client.GetStringAsync("/blog?page=2");
    Check(pageTwo.Contains("rel=\"canonical\" href=\"https://portfolio.example/blog?page=2\""), "paginated blog has its own canonical URL");
    Check((await client.GetAsync("/blog?page=999")).StatusCode == HttpStatusCode.NotFound, "empty pagination does not create indexable success pages");
    posts.RemoveAll(p => extra.Any(x => x.Id == p.Id));
    translated.Status = "archived";
    Check(!(await client.GetStringAsync("/blog/test-note")).Contains("hreflang=\"fa\""), "archiving a translation removes alternate links on the next request");
    posts.RemoveAll(p => p.Id != publishedPost.Id);
    var persianHome = await client.GetStringAsync("/fa");
    Check(!persianHome.Contains("fonts.googleapis.com") && persianHome.Contains("google-site-verification\" content=\"verification-fixture\""), "Persian page avoids unused external font CSS and supports Search Console verification");
    Check(persianHome.Contains("/app.css?v=") && persianHome.Contains("/animations.js?v="), "static asset URLs are content-versioned for safe caching");
    var compressedRequest = new HttpRequestMessage(HttpMethod.Get, "/fa"); compressedRequest.Headers.AcceptEncoding.ParseAdd("br");
    var compressed = await client.SendAsync(compressedRequest);
    Check(compressed.Content.Headers.ContentEncoding.Contains("br"), "public HTML supports Brotli compression");
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
    var internalLinks = ArticleMarkdown.Render("[related](/blog/related-note) [unsafe](//evil.example) [encoded](/%2fevil.example)");
    Check(internalLinks.Contains("href=\"/blog/related-note\"") && !internalLinks.Contains("href=\"//evil") && !internalLinks.Contains("href=\"/%2f"), "Markdown preserves internal links and rejects disguised external URLs");
    Console.WriteLine($"{checks} checks passed.");
}
catch (Exception error) { lock (logs) Console.Error.WriteLine(string.Join(Environment.NewLine, logs.TakeLast(35))); Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
finally { if (!server.HasExited) server.Kill(true); await fake.StopAsync(); }
