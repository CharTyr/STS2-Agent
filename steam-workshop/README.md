# STS2 AI Agent Steam Workshop publishing

This folder stores the versioned source assets and listing copy for the Steam Workshop item. Generated upload folders stay under `build/steam-workshop` and are not committed.

The uploaded item contains `STS2AIAgent.dll`, `STS2AIAgent.pck`, `STS2AIAgent.json`, `README.md`, and `LICENSE`. `STS2AIAgent.json` is a namespaced copy of the normal release package manifest. The player README comes from `content-readme.md`. The optional Python MCP server remains in the full GitHub release, not the Workshop item.

## Listing assets

| File | Use |
| --- | --- |
| `description.en.txt` | Steam BBCode. Packaged as the default Workshop description. |
| `description.zh-CN.txt` | Steam BBCode. Paste into the Simplified Chinese listing after upload. |
| `preview.jpg` | SteamCMD preview. 800x800 and under 1 MB. |
| `image.png` | Official ModUploader preview. 800x800 and under 1 MB. |
| `preview.png` | High-quality source. Do not upload; it is over 1 MB. |
| `workshop.json` | Title, tags, and ModUploader metadata. |
| `content-readme.md` | Player-facing README copied into the uploaded item. |
| `previews/` | Optional extra screenshots, each under 1 MB. Omit the folder until you have images. |

Workshop tags: `Tools & APIs`, `Utility`, `QoL`.

## Package

Build an upload-ready folder from the repository root. Package from a clean release tag, not a dirty worktree:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\package-steam-workshop.ps1" -Configuration Release
```

The output directory is both a SteamCMD payload and a Mega Crit [ModUploader](https://github.com/megacrit/sts2-mod-uploader) workspace:

```text
build/steam-workshop/sts2-ai-agent-vX.Y.Z/
  workshop.json
  image.png
  content/
  steam-workshop.vdf
```

The first upload must use `PublishedFileId` 0 and private visibility. Steam returns the item ID. Use that ID for each later update:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\package-steam-workshop.ps1" -Configuration Release -PublishedFileId "<Steam Workshop item ID>" -Visibility public -ChangeNote "v0.9.2"
```

Accept the [Steam Workshop legal agreement](https://steamcommunity.com/sharedfiles/workshoplegalagreement) before uploading. Never commit Steam credentials, Steam Guard codes, or the published Workshop item ID.

## Upload

Prefer the official ModUploader. It writes tags, optional extra previews, and the listing description:

```text
ModUploader.exe upload -w "<absolute path to sts2-ai-agent-vX.Y.Z>"
```

SteamCMD also works, but does not set tags:

```text
steamcmd +login <your-Steam-account> +workshop_build_item "<absolute path to steam-workshop.vdf>" +quit
```

After a SteamCMD upload, set the tags on the Steam item page to `Tools & APIs`, `Utility`, and `QoL`.

In either case, paste `description.zh-CN.txt` into the Simplified Chinese listing on the Steam page.

## Publisher checklist

1. Synchronize mod, API, MCP package, and lockfile versions before packaging.
2. Package from a clean release commit or tag.
3. Upload privately. Subscribe from a separate test account.
4. Launch with **Play with Mods**, accept the untrusted-code warning, enable the mod, and restart.
5. Confirm the mod appears exactly once, opens with F8, and reports the expected version. Remove any leftover manual copies from `mods/` first.
6. Confirm the English listing uses the BBCode copy, `preview.jpg` / `image.png` is selected, and the item stays free.
7. Paste the Simplified Chinese listing and set tags if the uploader did not.
8. Keep the GitHub source and AGPL-3.0-only license links.
9. Change the item to public only after the private subscription test passes.

The Workshop item provides the in-game overlay only. It does not contain an API key, LLM account, or hosted model service. Players configure their own OpenAI-compatible provider locally, then can invite an AI teammate (本地1人、1ai) from the main menu.
