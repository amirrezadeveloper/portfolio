using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace portfolio.Blog;

public static class ArticleMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UsePipeTables().Build();

    public static string Render(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            // Permit root-relative internal links without enabling protocol-relative or executable URLs.
            var url = link.Url ?? "";
            var decoded = Uri.UnescapeDataString(url);
            var internalLink = url.StartsWith('/') && !decoded.StartsWith("//") && !decoded.Contains('\\')
                && !decoded.Any(char.IsControl);
            if (!internalLink && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
                link.Url = "#";
        }
        foreach (var link in document.Descendants<AutolinkInline>())
        {
            if (link.IsEmail || !Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                link.Url = "#";
        }
        return document.ToHtml(Pipeline);
    }
}
