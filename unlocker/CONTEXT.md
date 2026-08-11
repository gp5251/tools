# Unlocker

A Windows shell tool that adds two verbs to the right-click context menu — "Force Delete" (强力删除) and "Unlock" (解除占用) — for targets that are locked by other processes.

## Language

**Force Delete** (强力删除):
The user-facing operation exposed in the Explorer context menu. Deletes the selected target even when it is in use, by walking the Deletion Ladder.
_Avoid_: hard delete, super delete

**Deletion Ladder** (删除阶梯):
The escalation policy behind every Force Delete: (1) Normal Delete, (2) Handle Release and retry, (3) user-confirmed Process Termination and retry, (4) user-confirmed Reboot Delete. A process is terminated ONLY after explicit user confirmation naming the affected processes — never silently.
_Avoid_: force-close, kill-and-delete

**Permanent Delete** (永久删除):
Deletion that bypasses the Recycle Bin, equivalent to SHIFT+Delete. Every rung of the Deletion Ladder performs Permanent Delete — a Force Delete never goes to the Recycle Bin.
_Avoid_: hard delete

**Normal Delete** (普通删除):
Rung 1 of the Deletion Ladder. A direct Permanent Delete attempt with no prior interference with other processes.

**Handle Release** (句柄释放):
Closing the handles that foreign processes hold open on the target. The shared mechanism behind rung 2 of the Deletion Ladder and the Unlock operation.
_Avoid_: unlocking (as a name for this mechanism; Unlock is the operation)

**Unlock** (解除占用):
The second user-facing operation in the Explorer context menu. Frees the target completely WITHOUT deleting it: Handle Release first, then user-confirmed Process Termination for Image Locks. Conceptually, Force Delete = Unlock + Permanent Delete; each Unlock mechanism corresponds to a Deletion Ladder rung.
_Avoid_: 解锁, release lock

**Reboot Delete** (重启删除):
Rung 4 of the Deletion Ladder and last resort. Scheduling the target for deletion at the next system boot — the tool itself NEVER initiates a reboot. Requires explicit user confirmation before being scheduled; UI text always says 下次重启时删除 to make the timing unambiguous.
_Avoid_: pending delete, delayed delete

**Image Lock** (映像锁定):
A lock the kernel holds on files mapped as executable images — a running exe or a loaded DLL. Invisible in the handle table, so Handle Release cannot touch it; detected by matching the loaded modules of running processes against the target.

**Process Termination** (结束进程):
Rung 3 of the Deletion Ladder. Terminating the processes whose Image Locks pin the target, then retrying deletion. Always requires explicit user confirmation listing every process to be killed, including unsuspecting hosts (e.g. explorer.exe hosting a shell extension).
_Avoid_: kill, force-close

**Locking Process** (占用进程):
A process holding at least one open handle on the target, causing Normal Delete to fail.

**Target** (目标):
The single file or folder selected in Explorer when either verb (Force Delete or Unlock) is invoked. A folder Target is processed recursively — for Force Delete, every locked file inside is walked through the Deletion Ladder; for Unlock, every file inside is scanned for handles. Escalation/confirmation dialogs are aggregated ONCE per invocation, never one dialog per file. Multi-selection is not supported.

**Confirmation Gate** (确认门):
Every Force Delete begins with a confirmation dialog stating the deletion is permanent and unrecoverable. No rung executes before the user confirms at the gate. Escalation rungs have their own additional confirmations.
