namespace portfolio.Blog;

// Shared only within one HTTP request: publishing/archive changes are visible on the next request.
public sealed class PublishedPostCatalog(BlogClient blog)
{
    private Task<IReadOnlyList<BlogPost>>? pending;
    public Task<IReadOnlyList<BlogPost>> GetAsync() => pending ??= blog.ListPublishedMetadataAsync();

    public static IReadOnlyList<BlogPost> Related(BlogPost post, IEnumerable<BlogPost> candidates) => candidates
        .Where(p => p.Language == post.Language && p.Id != post.Id && p.Slug != post.Slug)
        .Select(p => new { Post = p, Score = p.Tags.Intersect(post.Tags, StringComparer.OrdinalIgnoreCase).Count() })
        .OrderByDescending(p => p.Score).ThenByDescending(p => p.Post.PublishedAt)
        .Take(3).Select(p => p.Post).ToArray();
}
