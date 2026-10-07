# Amirreza Ghahremani · Portfolio

ASP.NET Core 10 Blazor portfolio aligned with the supplied résumé. The page uses server rendering, with a small browser script for navigation, active section tracking, and optional scroll reveal.

## Run locally

```powershell
dotnet run --project portfolio.csproj
```

Open the URL printed by `dotnet run`. The only prerequisite is the .NET 10 SDK.

## Content and assets

- Edit the page sections in `Components/Sections/`.
- Update styles in `wwwroot/app.css` and navigation behavior in `wwwroot/animations.js`.
- The supplied profile photograph is `wwwroot/profile.jpg`.
- Contact links are in `Components/Sections/ContactSection.razor`.

The enterprise project cards intentionally do not link to a generic GitHub profile or unrelated demo. Add a project-specific URL only when it is available and approved for public sharing.

## Blog

- `/blog` and `/fa/blog`: public English and Persian article lists.
- `/blog/{slug}` and `/fa/blog/{slug}`: published article pages.
- `/admin/login`: sign in with the Supabase email/password account.
- `/admin`: draft, published, and archived articles; `/admin/posts` starts a new article.
- The editor supports Markdown, code fences, tables, preview, draft save, publish, and archive. A draft save hides an already published article. Each language has its own unique slug.
- Text is saved temporarily in this browser's local storage. A successful database save clears that copy. On a shared computer, avoid leaving private drafts in browser storage.

The connected Supabase project is `portfolio-blog` (`ikcepreljlqgzbvudotr`). `appsettings.json` contains its **public publishable key**, never a secret or service-role key. Override with `Supabase__Url` and `Supabase__PublishableKey` to use another project. Auth uses the project's REST API. CRUD sends the signed-in user's token so database RLS remains authoritative. The `public.is_blog_admin()` RPC uses invoker permissions to check the private admin membership table. Removing membership rejects an existing site session on its next admin request.

Sessions expire with the Supabase access token, capped at one hour; sign in again after expiry. Tokens are stored only inside an encrypted, HttpOnly authentication cookie. Forms use ASP.NET antiforgery validation. Raw HTML and non-HTTP(S) Markdown links are disabled.

For local use, run `dotnet run` (the launch profile enables Development). Local Data Protection keys are stored in ignored `.local/keys`. For production, keep ASP.NET Data Protection keys in a protected persistent directory, shared between instances, and set `DataProtection__KeyPath` to that directory. If the host has no persistent directory, provide a supported shared Data Protection key store before scaling out; ephemeral instance keys invalidate sessions and antiforgery cookies across instances. Production cookies require HTTPS.

Supabase setup applied three remote migrations: `create_portfolio_blog`, `streamline_blog_post_policies`, and `add_blog_admin_check`. The already applied SQL is saved for reference in `docs/supabase-blog.sql`; do not rerun it on this project. Keep public signups and anonymous sign-ins disabled in Authentication → Sign In / Providers. Image upload is not included; Markdown can display an existing public HTTP(S) image URL. No AI or automation service is required.

## Verification

```powershell
dotnet build portfolio.csproj
dotnet run --project tests/BlogFlowChecks/BlogFlowChecks.csproj
```

The flow checker starts an isolated Supabase HTTP double and the real site on ports 5192/5193. It tests login, non-admin rejection, antiforgery, draft visibility, preview, publish, archive, duplicate slugs, language isolation, Markdown safety, upstream failures, and revoked admin membership. It never writes test articles to the connected cloud database. Supabase RLS was also checked with rollback-only SQL transactions.

Real account login must be checked with your password in the browser; do not send it in chat. The current Supabase security advisor reports a warning that leaked-password protection is disabled; review availability under [Password security](https://supabase.com/docs/guides/auth/password-security#password-strength-and-leaked-password-protection).
