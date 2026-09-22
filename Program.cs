using portfolio.Components;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/robots.txt", (HttpRequest request) =>
    Results.Text($"User-agent: *\nAllow: /\nSitemap: {GetPublicOrigin(request)}/sitemap.xml\n", "text/plain"));

app.MapGet("/sitemap.xml", (HttpRequest request) =>
{
    var origin = GetPublicOrigin(request);
    var sitemap = $"""
<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url><loc>{origin}/</loc></url>
  <url><loc>{origin}/fa</loc></url>
</urlset>
""";

    return Results.Text(sitemap, "application/xml");
});

app.MapRazorComponents<App>();

app.Run();

static string GetPublicOrigin(HttpRequest request)
{
    var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
    var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value;
    return $"{scheme}://{host}";
}
