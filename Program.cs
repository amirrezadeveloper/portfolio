using portfolio.Components;
using portfolio.Blog;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using portfolio.Seo;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

var initializeDataProtection = args.Contains("--initialize-data-protection");
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--initialize-data-protection").ToArray());

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Console logging works in local and hosted environments without Windows Event Log permissions.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddRazorComponents();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    // Opt in only when this container's ingress is owned by the hosting platform.
    // Vercel supplies these headers on the private HTTP hop after terminating HTTPS.
    if (builder.Configuration.GetValue<bool>("ReverseProxy:TrustPlatformHeaders"))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});
var protection = builder.Services.AddDataProtection().SetApplicationName("portfolio-blog");
if (builder.Environment.IsDevelopment())
    protection.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".local", "keys")));
else if (builder.Configuration["DataProtection:KeyPath"] is { Length: > 0 } keyPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
if (builder.Configuration.GetValue<bool>("DataProtection:ReadOnlyKeys") && !initializeDataProtection)
    protection.DisableAutomaticKeyGeneration();
builder.Services.AddRazorPages(options => options.Conventions.AuthorizeFolder("/Admin"))
    .AddMvcOptions(options => options.Filters.Add<ExpiredLoginFormFilter>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddOptions<SeoOptions>().Bind(builder.Configuration.GetSection("Seo"))
    .Validate(o => Uri.TryCreate(o.SiteUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
        && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment), "Seo:SiteUrl must be an absolute site origin.")
    .ValidateOnStart();
builder.Services.AddSingleton<SiteMetadata>();
builder.Services.AddScoped<PublishedPostCatalog>();
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection("Supabase"));
builder.Services.AddHttpClient<BlogClient>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<BlogCookieEvents>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/admin/login";
    options.AccessDeniedPath = "/admin/login";
    options.Cookie.Name = "portfolio.blog";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.SlidingExpiration = false;
    options.EventsType = typeof(BlogCookieEvents);
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("blog-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
if (initializeDataProtection)
{
    if (string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:KeyPath"]))
        throw new InvalidOperationException("Set DataProtection:KeyPath before initializing the private key ring.");
    // Called once while building the private container image. Never emit key material.
    app.Services.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("portfolio-key-initialization").Protect("initialize");
    return;
}
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseWhen(context => !context.Request.Path.StartsWithSegments("/admin"), branch => branch.UseResponseCompression());
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        // Versioned CSS/JS URLs can be cached; fonts change infrequently.
        context.Context.Response.Headers.CacheControl = context.File.Name.EndsWith(".woff2")
            ? "public,max-age=604800" : "public,max-age=3600";
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "same-origin";
    }
    await next();
});
app.UseAntiforgery();

app.MapGet("/robots.txt", (SiteMetadata site) =>
    Results.Text($"User-agent: *\nAllow: /\nDisallow: /admin\nSitemap: {site.Url("/sitemap.xml")}\n", "text/plain"));

app.MapGet("/sitemap.xml", async (PublishedPostCatalog catalog, SiteMetadata site, HttpResponse response) =>
{
    try { return Results.Text(site.Sitemap(await catalog.GetAsync()), "application/xml", System.Text.Encoding.UTF8); }
    catch (Exception e) when (e is BlogApiException or HttpRequestException or TaskCanceledException)
    {
        response.Headers.RetryAfter = "300";
        return Results.StatusCode(503);
    }
});

app.MapRazorComponents<App>();
app.MapRazorPages();

app.Run();
