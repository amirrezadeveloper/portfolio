using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using portfolio.Blog;

namespace portfolio.Seo;

public sealed class SeoOptions
{
    public string SiteUrl { get; set; } = "https://www.amirrezaghahremani.ir";
    public string GoogleSiteVerification { get; set; } = "";
}

public sealed class SiteMetadata(IOptions<SeoOptions> options)
{
    public const string AuthorName = "Amirreza Ghahremani";
    public string Origin => new Uri(options.Value.SiteUrl, UriKind.Absolute).GetLeftPart(UriPartial.Authority);
    public string Url(string path) => Origin + (path.StartsWith('/') ? path : "/" + path);
    public static string HomePath(string language) => language == "fa" ? "/fa" : "/";
    public static string BlogPath(string language, int page = 1) => (language == "fa" ? "/fa/blog" : "/blog") + (page > 1 ? "?page=" + page : "");
    public static DateTimeOffset Modified(BlogPost post) => post.UpdatedAt > post.PublishedAt ? post.UpdatedAt : post.PublishedAt ?? post.CreatedAt;

    public string ArticleJson(BlogPost post)
    {
        var canonical = Url(post.Url);
        var language = post.Language;
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["@type"] = "BlogPosting", ["@id"] = canonical + "#article",
                    ["headline"] = post.Title, ["description"] = post.Excerpt,
                    ["url"] = canonical, ["mainEntityOfPage"] = canonical,
                    ["inLanguage"] = language, ["datePublished"] = post.PublishedAt,
                    ["dateModified"] = Modified(post), ["keywords"] = post.Tags,
                    ["author"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "Person", ["@id"] = Url("/#person"),
                        ["name"] = AuthorName, ["url"] = Url(HomePath(language))
                    }
                },
                new Dictionary<string, object?>
                {
                    ["@type"] = "BreadcrumbList", ["itemListElement"] = new object[]
                    {
                        Crumb(1, language == "fa" ? "خانه" : "Home", Url(HomePath(language))),
                        Crumb(2, language == "fa" ? "بلاگ" : "Blog", Url(BlogPath(language))),
                        Crumb(3, post.Title, canonical)
                    }
                }
            }
        });
    }

    private static object Crumb(int position, string name, string item) => new Dictionary<string, object?>
    { ["@type"] = "ListItem", ["position"] = position, ["name"] = name, ["item"] = item };

    public string Sitemap(IReadOnlyList<BlogPost> posts)
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        var root = new XElement(ns + "urlset", new XAttribute(XNamespace.Xmlns + "xhtml", xhtml));
        var bySlug = posts.ToLookup(p => p.Slug, StringComparer.Ordinal);
        foreach (var path in new[] { "/", "/fa", "/blog", "/fa/blog" })
            root.Add(new XElement(ns + "url", new XElement(ns + "loc", Url(path))));
        foreach (var post in posts)
        {
            var entry = new XElement(ns + "url", new XElement(ns + "loc", Url(post.Url)),
                new XElement(ns + "lastmod", Modified(post).ToString("O")));
            var translations = bySlug[post.Slug].ToArray();
            if (translations.Any(p => p.Language != post.Language))
            {
                foreach (var translation in translations)
                    entry.Add(new XElement(xhtml + "link", new XAttribute("rel", "alternate"),
                        new XAttribute("hreflang", translation.Language), new XAttribute("href", Url(translation.Url))));
                var english = translations.FirstOrDefault(p => p.Language == "en");
                if (english != null) entry.Add(new XElement(xhtml + "link", new XAttribute("rel", "alternate"),
                    new XAttribute("hreflang", "x-default"), new XAttribute("href", Url(english.Url))));
            }
            root.Add(entry);
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }
}
