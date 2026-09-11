# STS2 AI Agent Steam Workshop publishing

This folder stores the versioned source assets and listing copy for the Steam Workshop item. Generated upload folders stay under `build/steam-workshop` and are not committed.

The uploaded item contains `STS2AIAgent.dll`, `STS2AIAgent.pck`, `STS2AIAgent.json`, `README.md`, and `LICENSE`. `STS2AIAgent.json` is a namespaced copy of the normal release package manifest. The player README comes from `content-readme.md`. In-game MCP is in the Workshop item (overlay Connect tab). The optional Python sidecar remains GitHub-only.

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

For a first upload, `PublishedFileId` 0 defaults to private visibility. Steam returns the item ID. Reuse that ID for every later update; a nonzero ID defaults to public with the script from commit `96bd410` onward. Explicit `-Visibility` always wins. The generated `workshop.json` and VDF use the resolved script value, not just the source JSON default.

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\package-steam-workshop.ps1" -Configuration Release -PublishedFileId "<Steam Workshop item ID>" -Visibility public -ChangeNote "v0.10.5: reward overlay, native game-over save wait, budget and recovery fixes"
```

The `v0.10.5` tag predates the default-visibility fix. When packaging from that tag, pass `-Visibility public` explicitly for an existing public item. Always provide a version-specific `-ChangeNote` for updates: the script otherwise uses `Initial Workshop upload` and overwrites the source JSON change note in generated output.

Accept the [Steam Workshop legal agreement](https://steamcommunity.com/sharedfiles/workshoplegalagreement) before uploading. Never commit Steam credentials or Steam Guard codes. A published item ID is a public identifier; keep it stable across updates and check it before uploading.

## Upload

Prefer the official ModUploader. It writes tags, optional extra previews, and the listing description:

```text
ModUploader.exe upload -w "<absolute path to sts2-ai-agent-vX.Y.Z>"
```

Restart the Steam client immediately before uploading. Without a restart, `SubmitItemUpdate` can sit in `k_EItemUpdateStatusPreparingConfig` / `k_EItemUpdateStatusPreparingContent` indefinitely and never reach `UploadingContent`; on 2026-09-11 the same command finished in 16 seconds right after a Steam restart. This is not a proxy problem: Steam-facing domains were intermittently unreachable both directly and through the local proxy, and the restart alone was the fix.

Updates must target the existing item. Pass `--id <item ID>` or keep `mod_id.txt` in the workspace — the uploader creates a new item when neither is present, and it writes `mod_id.txt` after a successful upload. Current item: `3796486050`.

SteamCMD also works, but does not set tags:

```text
steamcmd +login <your-Steam-account> +workshop_build_item "<absolute path to steam-workshop.vdf>" +quit
```

After a SteamCMD upload, set the tags on the Steam item page to `Tools & APIs`, `Utility`, and `QoL`.

In either case, paste `description.zh-CN.txt` into the Simplified Chinese listing on the Steam page.

## Publisher checklist

1. Synchronize mod, API, MCP package, and lockfile versions before packaging.
2. Package from a clean release commit or tag.
3. For a new item, upload privately and test subscription with an account that can access it. For an existing public item, update the same ID with public visibility; do not make the item private as a routine update step.
4. Launch with **Play with Mods**, accept the untrusted-code warning, enable the mod, and restart.
5. Confirm the mod appears exactly once, opens with F8, and reports the expected version. Back up and remove only this mod's leftover manual copies from `mods/` before testing subscription loading.
6. Confirm the English listing uses the BBCode copy, `preview.jpg` / `image.png` is selected, and the item stays free.
7. Paste the Simplified Chinese listing and set tags if the uploader did not.
8. Keep the GitHub source and AGPL-3.0-only license links.
9. For a new item, make it public after the initial subscription test passes. After every update, verify the item's visibility, downloaded manifest version, and actual game loading separately; successful upload or download alone is not a loading test.

Current release and channel evidence is maintained in [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md). This guide describes the publishing workflow, not a claim that all installation checks have passed.

The Workshop item provides the in-game overlay only. It does not contain an API key, LLM account, or hosted model service. Players configure their own OpenAI-compatible provider locally, then can invite an AI teammate (本地1人、1ai) from the main menu.
