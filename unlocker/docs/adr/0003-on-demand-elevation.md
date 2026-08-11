# On-demand elevation instead of requireAdministrator

The engine runs as-invoker by default and re-launches itself elevated (`runas`) only when the Deletion Ladder escalates past Normal Delete. Rung 1 on the user's own files needs no privileges, so the common case never shows a UAC prompt; Handle Release (needs `SeDebugPrivilege`) and Reboot Delete (needs admin for pending-rename operations) do. The cost is a process-relaunch dance that must carry the target path and current rung as state — accepted as the price of keeping UAC prompts honest and rare.

## Considered Options

- **On-demand elevation** (chosen) — UAC only when escalation genuinely needs it; more complex process model.
- **requireAdministrator manifest** — trivial to implement; UAC on every single Force Delete, including ones needing no privilege.
- **Elevated background service** — silent; a disproportionate attack surface and install burden for a shell utility.
