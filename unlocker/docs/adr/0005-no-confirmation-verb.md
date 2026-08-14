# Unattended Delete: Ctrl+click bypasses every confirmation

**Status**: accepted — amended same-day: invocation folded from a separate 强力删除(无需确认) verb into Ctrl+click on 强力删除 (the menu was getting crowded). The short-lived third verb is unregistered on upgrade. Amends [ADR-0002](0002-deletion-policy.md)/[ADR-0004](0004-confirmed-process-termination.md) for this gesture only.

Holding Ctrl while clicking 强力删除 walks the entire Deletion Ladder with zero prompts — the Confirmation Gate, Handle Release, Process Termination, and Reboot Delete confirmations are ALL skipped (`--yes`). The new process reads the physically-held key via `GetAsyncKeyState` at startup (Explorer launches classic verbs synchronously, so Ctrl is still down from the click); escalation past Normal Delete carries the decision to the elevated instance as the `--yes` flag, because the user has long released Ctrl by the time UAC clears. Rationale for skipping even Process Termination: an unattended mode that still prompted whenever a file is actually locked would be useless in exactly the scenario the tool exists for, and holding Ctrl is itself explicit authorization. This contracts away the "never kill silently" guarantee of ADR 0002/0004 for the gesture only — plain 强力删除 keeps every confirmation unchanged. Two outcomes still show a notice (not a choice) even unattended: items scheduled for Reboot Delete, and total failure — in both cases the target visibly remains on disk, so silence would read as success. Known limitation: the Ctrl read happens after process start (~0.5 s), so a Ctrl tap quicker than that degrades to a normal confirmed delete, never to an unintended unattended one.

## Considered Options

- **Ctrl+click on the existing verb** (chosen) — one menu entry, two modes; modifier is read at startup and survives escalation as `--yes`.
- **Separate 无需确认 verb** (tried, abandoned) — works identically but clutters a menu that is already one level deep on Windows 11.
- **Skip only the Confirmation Gate, keep escalation confirmations** — safer, but every genuinely-locked target still interrupts, defeating the gesture's purpose.
