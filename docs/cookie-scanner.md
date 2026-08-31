# Cookie scanner

**The scanner is no longer in this repository.** It was extracted into the `Esatto.Packages`
mono-repo, where it lives as four packages plus a desktop exe. The full documentation — the six
passes, the flags, the API user, the client secret, publishing, known limitations and the
verification log — moved with it:

📄 **`c:\src\Esatto.Packages\docs\cookie-scanner.md`**

## What this site still has

Nothing of the scanner's own code. The merge endpoint that used to be `CookieScan/` here now arrives
as a package:

```xml
<PackageReference Include="Esatto.Umbraco.Backoffice.CookieScan" Version="..." />
```

It self-registers through its composer, so `Program.cs` holds only two scanner-related lines:

```csharp
// The package binds its options from Esatto:CookieScan:ApiUser. This site's have lived under
// NDSTK:CookieScanApiUser since before the package existed, so it binds the old section instead.
builder.Services.ConfigureCookieScanApiUser(
    builder.Configuration.GetSection("NDSTK:CookieScanApiUser"));

// After BootUmbracoAsync. Creates the API user if configured to; never throws.
await app.Services.SeedCookieScanApiUserAsync();
```

**The configuration section did not change.** `NDSTK:CookieScanApiUser` in
`appsettings.Development.json`, `appsettings.Production.json` and the untracked
`appsettings.Secrets.json` is untouched, as is the production `NDSTK__CookieScanApiUser__ClientSecret`
environment variable. That was the point of binding the section explicitly: renaming settings on a
live site is a deployment change, and this way there was none.

## Scanning this site

```
dotnet tool install -g Esatto.CookieScan.Cli
esatto-cookiescan --url https://ndstk.se --consent-cookie ndstk-consent --client-id cookie-scanner
```

**`--consent-cookie ndstk-consent` is not optional for this site.** The shipped catalogue names
`cookie-consent`, the `CookieBannerOptions.CookieName` default, and this site overrides it. Omit the
flag and one run produces two false findings: `ndstk-consent` goes unrecognised, takes the
catalogue's `marketing` fallback, is seen on the reject-all pass and is reported as a **violation**,
while `cookie-consent` is simultaneously reported as declared-but-never-found.

The dashboard has the same setting as a field on the run card, remembered per site profile — so once
it is saved for this site, it is filled in for every later run.

The environment variable carrying the client secret is now
**`ESATTO_COOKIESCAN_CLIENT_SECRET`**, renamed with the tool. The old
`NDSTK_COOKIESCAN_CLIENT_SECRET` is no longer read.
