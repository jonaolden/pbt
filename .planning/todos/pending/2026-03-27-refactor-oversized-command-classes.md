---
created: 2026-03-27T21:40:30.047Z
title: Refactor oversized command classes
area: architecture
files:
  - src/Pbt/Commands/ImportCommand.cs
  - src/Pbt/Commands/BuildCommand.cs
---

## Problem

ImportCommand.cs (757 lines) and BuildCommand.cs (577 lines) contain significant business logic that belongs in services. This makes them hard to test (coupled to console I/O) and violates single responsibility.

BuildCommand.ExecuteCoreBuild is 250+ lines with high parameter count.

## Solution

Extract business logic into Pbt.Core services:
- BuildCommand → BuildService (orchestration), TmdlOutputGenerator, PbipOutputGenerator
- ImportCommand → ImportService, merge logic consolidation (duplicated between CSV and TMDL paths)

Keep commands as thin wrappers: parse args → call service → format output.
