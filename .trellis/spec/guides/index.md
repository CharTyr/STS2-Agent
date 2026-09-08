# Shared Thinking Guides

Use these short checklists alongside the executable contracts in the layer specs.

- [Cross-layer changes](cross-layer-thinking-guide.md): state/action projections, multiple MCP surfaces, and release metadata.
- [Code reuse](code-reuse-thinking-guide.md): existing owners, shared helpers, and intentional cross-language duplication.

## Pre-Development Checklist

Read the [Mod](../mod/index.md), [MCP](../mcp/index.md), or [Operations](../operations/index.md) index for the code being changed. Use the cross-layer guide when a field or action has several consumers; use the reuse guide before adding a helper or changing a repeated constant.

## Quality Check

Trace changed fields to their consumers and verify references against source. Keep these guides focused on questions; detailed signatures, errors, and test commands belong in the layer specs.
