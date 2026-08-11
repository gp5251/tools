# Confirmed Process Termination rung (amends ADR 0002)

The Deletion Ladder gains a rung between Handle Release and Reboot Delete: when targets remain locked because running executables hold them via Image Locks (kernel image sections — invisible to the handle table, so Handle Release cannot touch them), the tool detects the owning processes by scanning loaded modules and offers to terminate them. Termination requires explicit user confirmation naming every affected process each time — never silent, never automatic. This amends ADR 0002's blanket "NEVER terminates a process": the hazard ADR 0002 guarded against was killing processes WITHOUT user intent (a database, explorer.exe); a named, confirmed kill preserves that protection while covering the common real-world case of deleting a folder containing a running exe. System-critical processes (csrss, lsass, wininit, …) are hard-excluded regardless of confirmation.

The same confirmed-termination mechanism is available in Unlock: terminating a process is sometimes the ONLY way to free a file (Image Locks), and Unlock without it is incomplete. Conceptually Force Delete = Unlock + Permanent Delete, both verbs sharing one confirmed-termination mechanism.

## Considered Options

- **Confirmed termination rung** (chosen) — covers running-exe locks; user retains the final word each time.
- **Keep never-kill** — running exes could only ever be reboot-deleted; real-world usage proved this reads as "the tool doesn't work".
- **Automatic kill** — rejected: same catastrophe class ADR 0002 was written to prevent.
