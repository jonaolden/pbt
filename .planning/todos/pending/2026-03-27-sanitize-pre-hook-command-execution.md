---
created: 2026-03-27T21:40:30.047Z
title: Sanitize pre-hook command execution
area: security
files:
  - src/Pbt/Commands/BuildCommand.cs:271-288
---

## Problem

Pre-hook commands from user config are passed directly to `Process.Start()` without:
- Input sanitization (command injection risk)
- Timeout enforcement (malformed hook could hang build indefinitely)
- Null-check on process handle before `WaitForExit()`

While hooks are user-configured (not external input), defense-in-depth applies.

## Solution

1. Add a configurable timeout (e.g., 60s default) with `WaitForExit(timeout)`
2. Null-check process before calling WaitForExit/ExitCode
3. Consider documenting allowed hook patterns or adding a `--no-hooks` flag
