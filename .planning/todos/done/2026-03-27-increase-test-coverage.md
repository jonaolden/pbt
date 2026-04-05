---
created: 2026-03-27T21:40:30.047Z
title: Increase test coverage
area: testing
files:
  - src/Pbt.Core/Services/SmartMerger.cs
  - src/Pbt.Core/Services/TableGenerator.cs
  - src/Pbt.Core/Services/PbipGenerator.cs
  - src/Pbt.Core/Services/PbipValidator.cs
  - src/Pbt.Core/Services/TmdlTableImporter.cs
  - src/Pbt.Core/Services/CsvSchemaReader.cs
  - src/Pbt.Core/Services/AssetLoader.cs
---

## Problem

Structural test coverage is 5% (5 of 98 types have corresponding test classes). Critical services with no tests:
- SmartMerger — merge logic is complex and error-prone
- TableGenerator — column naming rules, regex matching
- PbipGenerator — PBIP structure generation
- PbipValidator — JSON schema validation
- TmdlTableImporter — TMDL parsing
- CsvSchemaReader — CSV header matching
- AssetLoader — path resolution

## Solution

Prioritize by risk: SmartMerger and TableGenerator first (complex business logic), then PbipGenerator/PbipValidator (output correctness), then importers.
