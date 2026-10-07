using System.ComponentModel.DataAnnotations;

namespace portfolio.Blog;

public sealed class BlogPost
{
    public Guid Id { get; set; }
    public Guid AuthorId { get; set; }
    [Required, StringLength(250)] public string Title { get; set; } = "";
    [Required, StringLength(160), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$")]
    public string Slug { get; set; } = "";
    [RegularExpression("^(fa|en)$")] public string Language { get; set; } = "fa";
    [StringLength(2000)] public string Excerpt { get; set; } = "";
    [Required, StringLength(100000)] public string ContentMarkdown { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public string Status { get; set; } = "draft";
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string Url => (Language == "fa" ? "/fa/blog/" : "/blog/") + Slug;
    public string DateLabel => PublishedAt?.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public int ReadingMinutes => Math.Max(1, (int)Math.Ceiling(ContentMarkdown.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length / 200d));
}

public sealed class SupabaseOptions
{
    public string Url { get; set; } = "";
    public string PublishableKey { get; set; } = "";
}

public sealed class BlogApiException(int statusCode) : Exception("The blog service could not complete the request.")
{
    public int StatusCode { get; } = statusCode;
}
