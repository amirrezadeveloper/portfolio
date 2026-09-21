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
