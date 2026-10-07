using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;

namespace portfolio.Blog;

// Reject stale login submissions and ask for a fresh form; never retry credentials.
public sealed class ExpiredLoginFormFilter : IAsyncAlwaysRunResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (context.Result is IAntiforgeryValidationFailedResult &&
            request.Path.Equals("/admin/login", StringComparison.OrdinalIgnoreCase) && request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(context.HttpContext.RequestAborted);
            if (!string.IsNullOrEmpty(form["__RequestVerificationToken"]))
                context.Result = new LocalRedirectResult("/admin/login?expired=1");
        }
        await next();
    }
}
