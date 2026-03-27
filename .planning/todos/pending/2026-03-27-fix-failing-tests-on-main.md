---
created: 2026-03-27T21:40:30.047Z
title: Fix failing tests on main
area: testing
files:
  - tests/Pbt.Core.Tests/ModelComposerTests.cs:43
  - tests/Pbt.Core.Tests/ExampleProjectFullPipelineTests.cs:325
---

## Problem

9 out of 79 tests fail on main branch. Two key failures:
- `ModelComposerTests.ComposeModel_WithExampleProject_ShouldCreateValidDatabase` — expected "SalesAnalytics" but got null. Suggests ModelComposer is not setting the database name correctly.
- `ExampleProjectFullPipelineTests.ExampleProject_LineageStability_ShouldReuseTagsOnRebuild` — "First build should generate new tags" assertion fails.

This indicates a regression in the core model composition pipeline. Tests on main should always pass.

## Solution

1. Investigate ModelComposer to find why database name returns null
2. Check if recent commits changed model composition or lineage tag generation logic
3. Fix root cause and verify all 79 tests pass
