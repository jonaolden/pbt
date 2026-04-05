---
created: 2026-03-27T21:40:30.047Z
title: Fix YAML silent property ignoring
area: validation
files:
  - src/Pbt.Core/Infrastructure/YamlSerializer.cs
---

## Problem

`IgnoreUnmatchedProperties()` in YamlSerializer means typos in YAML files are silently ignored. A user writing `table_names:` instead of `tables:` gets an empty list with no warning.

For a build tool, silent data loss from typos is a serious usability issue.

## Solution

Options:
1. Remove `IgnoreUnmatchedProperties()` and let YamlDotNet throw on unknown keys
2. Keep it but collect unmatched properties and emit warnings
3. Add a strict mode that errors on unmatched properties (default for `pbt validate`)
