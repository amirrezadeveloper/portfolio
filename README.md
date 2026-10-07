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

For local use, run `dotnet run` (the launch profile enables Development). Local Data Protection keys are stored in ignored `.local/keys`. Production cookies require HTTPS.

On Vercel, `Dockerfile.vercel` generates a private Data Protection key ring once during the image build, then copies it outside `wwwroot` into the private runtime image. All replicas and cold starts of that image read the same keys with automatic generation disabled. No key material is committed to Git, returned by the initialization command, or served as static content. Treat the built image as a private secret-bearing artifact. A newly built image can rotate the keys: active sessions then need a fresh sign-in, and stale login forms redirect to a fresh form with an explanation instead of a blank HTTP 400. The initialization command is `dotnet portfolio.dll --initialize-data-protection` with an explicit `DataProtection__KeyPath`.

For sessions that must also survive image rebuilds, mount a protected key directory shared between deployments and replicas, or configure another shared Data Protection store. Override `DataProtection__KeyPath` accordingly, and only set `DataProtection__ReadOnlyKeys=true` when an external primary owns key creation/rotation. Do not rely on an instance-local writable directory in a scaled deployment.

Supabase setup applied three remote migrations: `create_portfolio_blog`, `streamline_blog_post_policies`, and `add_blog_admin_check`. The already applied SQL is saved for reference in `docs/supabase-blog.sql`; do not rerun it on this project. Keep public signups and anonymous sign-ins disabled in Authentication → Sign In / Providers. Image upload is not included; Markdown can display an existing public HTTP(S) image URL. No AI or automation service is required.

## Verification

```powershell
dotnet build portfolio.csproj
dotnet run --project tests/BlogFlowChecks/BlogFlowChecks.csproj
dotnet run --project tests/BlogFlowChecks/BlogFlowChecks.csproj -- --production-proxy
dotnet run --project tests/BlogFlowChecks/BlogFlowChecks.csproj -- --shared-keys
```

The flow checker starts an isolated Supabase HTTP double and the real site on ports 5192/5193. It tests login, non-admin rejection, antiforgery, draft visibility, preview, publish, archive, duplicate slugs, language isolation, Markdown safety, upstream failures, and revoked admin membership. It never writes test articles to the connected cloud database. Supabase RLS was also checked with rollback-only SQL transactions.

The production proxy check reproduces HTTPS termination followed by a private HTTP hop. Forwarded headers run before HTTPS redirection, authentication, and antiforgery so secure cookies and authentication redirects use the original HTTPS scheme. `Dockerfile.vercel` explicitly enables `ReverseProxy__TrustPlatformHeaders` for Vercel's managed ingress; do not enable this setting for a server directly reachable by arbitrary clients. Other hosts use ASP.NET's default trusted loopback proxy configuration unless configured otherwise.

The shared-keys check initializes a private image key ring, runs two separate Production processes, sends a form from the first process to the second, validates the resulting authentication cookie on the first, then restarts the second process and validates the same cookie again. It also verifies that expired login forms are rejected and redirected without processing their credentials. It uses isolated HTTP doubles and test credentials throughout.

Real account login must be checked with your password in the browser; do not send it in chat. The current Supabase security advisor reports a warning that leaked-password protection is disabled; review availability under [Password security](https://supabase.com/docs/guides/auth/password-security#password-strength-and-leaked-password-protection).
