# Deletion policy: ladder, permanent, never kill

**Status**: amended by [ADR-0004](0004-confirmed-process-termination.md) (process termination allowed with explicit per-invocation confirmation)

Every Force Delete walks the Deletion Ladder: Normal Delete, then Handle Release + retry, then — only with explicit user confirmation — Reboot Delete. Two deliberate constraints apply: deletion is ALWAYS permanent (never the Recycle Bin, matching SHIFT+Delete semantics, so every rung behaves identically), and the ladder NEVER terminates a Locking Process — killing a process the user didn't ask to kill (a database, explorer.exe) is a worse failure than leaving a file in place. These constraints are the safety contract of the tool; weakening either silently would betray user trust in both directions.

## Considered Options

- **Permanent delete always** (chosen) — one consistent outcome; irreversible by design.
- **Recycle Bin when possible** — undoable rung 1, but the same verb then has two different outcomes and rungs 2-3 cannot honor it anyway.
- **Kill Locking Processes as a rung** (rejected) — data-loss and system-stability risk without explicit user intent.
