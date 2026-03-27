---
created: 2026-03-27T21:40:30.047Z
title: Narrow broad catch(Exception) handlers
area: error-handling
files:
  - src/Pbt.Core/Infrastructure/YamlSerializer.cs
  - src/Pbt.Core/Services/LineageManifestService.cs
  - src/Pbt.Core/Services/PbipValidator.cs
  - src/Pbt.Core/Services/SmartMerger.cs
  - src/Pbt.Core/Services/TableRegistry.cs
  - src/Pbt.Core/Services/TmdlTableImporter.cs
  - src/Pbt.Core/Services/Validator.cs
  - src/Pbt/Commands/BuildCommand.cs
  - src/Pbt/Commands/DiffCommand.cs
  - src/Pbt/Commands/ImportCommand.cs
  - src/Pbt/Commands/InitCommand.cs
  - src/Pbt/Commands/LineageCommand.cs
  - src/Pbt/Commands/ListCommand.cs
  - src/Pbt/Commands/ValidateCommand.cs
---

## Problem

34 occurrences of `catch(Exception)` or bare `catch` across the codebase. These catch critical system exceptions (OutOfMemoryException, StackOverflowException) alongside expected failures. Roslyn anti-pattern AP005.

## Solution

Replace each with specific exception types relevant to the operation:
- YAML operations: `YamlException`
- File I/O: `IOException`, `UnauthorizedAccessException`
- JSON parsing: `JsonException`
- Process execution: `InvalidOperationException`

Tackle incrementally — start with services (Pbt.Core), then commands (Pbt).
