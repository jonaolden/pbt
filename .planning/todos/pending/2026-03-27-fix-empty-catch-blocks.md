---
created: 2026-03-27T21:40:30.047Z
title: Fix empty catch blocks
area: error-handling
files:
  - src/Pbt.Core/Services/PbipValidator.cs:209
  - src/Pbt/Commands/BuildCommand.cs:567
  - src/Pbt/Commands/DiffCommand.cs:107
  - src/Pbt/Commands/DiffCommand.cs:127
---

## Problem

4 empty catch blocks silently swallow exceptions (AP007 anti-pattern):
- `PbipValidator.cs:209` — `catch (JsonException) { }` — invalid JSON ignored during validation
- `BuildCommand.cs:567` — `catch (Exception) { }` — build errors lost
- `DiffCommand.cs:107,127` — `catch { }` — unparseable files skipped, breaking diff accuracy

In a build/validation tool, silently ignoring errors defeats the tool's purpose and hides real issues from users.

## Solution

At minimum, log warnings or return them as non-fatal diagnostics. For DiffCommand, report skipped files so users know the diff is incomplete.
