# Design

Keep game operations on the existing GameThread bridge. Put pure regression cases in the existing executable test project. Preserve HTTP and MCP fields and one-shot action semantics. Fix both state-diff implementations together. Treat companion payload local identity as the AI teammate identity. Retain snapshots only while subscribers exist. Save a theme separately from unsaved form edits, and keep disk failures visible.

Limit evidence to static analysis, deterministic regressions and offline builds. No visual or gameplay success claim follows from these checks.
