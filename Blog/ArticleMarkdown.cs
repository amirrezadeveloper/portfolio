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
            // Only web links and images are allowed. Raw HTML and executable URI schemes are disabled.
            if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
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
