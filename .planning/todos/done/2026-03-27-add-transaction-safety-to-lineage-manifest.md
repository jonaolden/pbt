---
created: 2026-03-27T21:40:30.047Z
title: Add transaction safety to lineage manifest
area: reliability
files:
  - src/Pbt.Core/Services/LineageManifestService.cs
---

## Problem

LineageManifestService.CleanOrphanedTags removes entries in memory but if the process crashes before save, the manifest is corrupted. No atomic write guarantee exists.

## Solution

Use atomic file writes: write to a temp file in the same directory, then `File.Move` with overwrite. This ensures the manifest is either fully written or unchanged. Also consider adding a `.bak` copy before destructive operations like `lineage reset`.
