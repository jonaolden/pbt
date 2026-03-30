---
created: 2026-03-27T21:40:30.047Z
title: Replace DateTime.UtcNow with TimeProvider
area: testing
files:
  - src/Pbt.Core/Services/LineageManifestService.cs:48
  - src/Pbt.Core/Services/LineageManifestService.cs:68
  - src/Pbt.Core/Services/LineageManifestService.cs:199
---

## Problem

3 direct uses of `DateTime.UtcNow` in LineageManifestService make timestamps untestable. AP004 anti-pattern.

## Solution

Inject `TimeProvider` via constructor and use `TimeProvider.GetUtcNow()`. Use `FakeTimeProvider` in tests for deterministic timestamp assertions.
