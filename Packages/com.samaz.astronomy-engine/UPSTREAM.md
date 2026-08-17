# Astronomy Engine upstream record

- Project: [cosinekitty/astronomy](https://github.com/cosinekitty/astronomy)
- Pinned release: `v2.1.19`
- Imported source: `source/csharp/astronomy.cs`
- Source URL: https://raw.githubusercontent.com/cosinekitty/astronomy/v2.1.19/source/csharp/astronomy.cs
- SHA-256: `475be41084beddf3eb13878adacb7c58c73d1864221464464ccf147c99cfa508`
- License: MIT; see `LICENSE.md` and the unmodified header in `Runtime/ThirdParty/astronomy.cs`.

The upstream source is intentionally unmodified. SAMAZ-specific code belongs in the
`Samaz.Observatory.Astronomy` assembly, so future upgrades can replace this file
atomically after verifying the version and checksum.
