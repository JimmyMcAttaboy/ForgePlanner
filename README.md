# Provisioner

A crafting planner for Valheim: plan what to make or upgrade, pin what you still have to gather, and track it against your chests and backpack.

**Provisioner is a fork of [ForgePlanner](https://github.com/Biozip/ForgePlanner) by Biozip** ([Thunderstore](https://thunderstore.io/c/valheim/p/Biozip/ForgePlanner/)). I made this version for my friends on my server. The planner, the recipe and smelting math and the item card are Biozip's work. This fork adds a storage snapshot for pinned materials, material bars, auto snapshot at home, owned gear and a reworked window. Changes are offered back as pull requests to ForgePlanner.

**Looking for a crafting planner? Use the original ForgePlanner.**

- On Thunderstore as **Provisioner**.
- Player page (what it does): [`provisioner/README.md`](provisioner/README.md)

## Branches

- `provisioner` (default): Provisioner releases.
- `main`: tracks ForgePlanner's `main`; pull requests to ForgePlanner branch from here.

## Building

Needs the .NET SDK and an installed Valheim with BepInEx 5.4.2350 or newer.

```
dotnet build -c Release -p:ValheimDir="D:\SteamLibrary\steamapps\common\Valheim" -p:BepInExDir="<profile>\BepInEx\core"
```

`VERSION` holds the release number and must match the top entry of `Versions.md`; the build stops if they differ.

## Releasing

```
dotnet build -c Release ...
bun provisioner/pack.ts      # dist/Provisioner-<version>.zip
bun provisioner/publish.ts   # Thunderstore; needs THUNDERSTORE_TEAM and a team token
bun provisioner/release.ts   # GitHub: tag provisioner-v<version>, release with the zip and changelog notes
```

Bump `VERSION`, `Versions.md`, `provisioner/manifest.json` and `provisioner/CHANGELOG.md` together, commit and push, then run the three steps. Every version is on Thunderstore and on GitHub Releases with the same zip.

## Licence

MIT. ForgePlanner is © 2026 Biozip; Provisioner's changes are © 2026 JimmyMcAttaboy. See [`LICENSE`](LICENSE).
