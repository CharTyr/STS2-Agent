# STS2 AI Agent Steam Workshop publishing

This folder stores the versioned source assets and listing copy for the Steam Workshop item. Generated upload folders stay under build/steam-workshop and are not committed.

The uploaded item contains STS2AIAgent.dll, STS2AIAgent.pck, STS2AIAgent.json, README.md, and LICENSE. STS2AIAgent.json is a namespaced copy of the normal release package manifest. The optional Python MCP server remains in the full GitHub release, not the Workshop item.

Build an upload-ready folder from the repository root:

    powershell -ExecutionPolicy Bypass -File ".\scripts\package-steam-workshop.ps1" -Configuration Release

The first upload must use PublishedFileId 0 and private visibility. Steam returns the item ID. Use that ID for each later update:

    powershell -ExecutionPolicy Bypass -File ".\scripts\package-steam-workshop.ps1" -Configuration Release -PublishedFileId "<Steam Workshop item ID>" -Visibility public -ChangeNote "v0.9.2"

Upload with a locally authenticated SteamCMD session:

    steamcmd +login <your-Steam-account> +workshop_build_item "<absolute path to steam-workshop.vdf>" +quit

Never commit Steam credentials, Steam Guard codes, or the published Workshop item ID.

Publisher checklist:

1. Synchronize mod, API, MCP package, and lockfile versions before packaging.
2. Upload privately and subscribe from a separate test account.
3. Confirm the mod appears exactly once, opens with F8, and reports the expected version.
4. Paste the English and Simplified Chinese listing copy into Steam and select preview.jpg. It is 800 by 800 pixels and under 1 MB; preview.png is the high-quality source asset.
5. Keep the item free and link to the source repository and AGPL-3.0-only license.
6. Change the item to public only after the private subscription test passes.

The Workshop item provides the in-game overlay only. It does not contain an API key, LLM account, or hosted model service. Players configure their own OpenAI-compatible provider locally.
