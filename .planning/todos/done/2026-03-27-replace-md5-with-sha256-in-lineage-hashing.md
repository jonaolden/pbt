---
created: 2026-03-27T21:40:30.047Z
title: Replace MD5 with SHA256 in lineage hashing
area: general
files:
  - src/Pbt.Core/Services/LineageManifestService.cs:344
---

## Problem

MD5 is used for deterministic GUID generation. While not used for security here, MD5 is deprecated and triggers audit warnings. SHA256 is the modern default.

## Solution

Replace `MD5.Create()` with `SHA256.Create()` and take first 16 bytes for the GUID. Existing lineage tags will change — document this as a breaking change or provide a migration path.
