---
created: 2026-03-27T21:40:30.047Z
title: Pre-compile and cache regex patterns
area: performance
files:
  - src/Pbt.Core/Services/RelationshipShorthandParser.cs
  - src/Pbt.Core/Services/NamingConverter.cs
  - src/Pbt.Core/Services/TableGenerator.cs
  - src/Pbt.Core/Services/SourceTypeMapper.cs
---

## Problem

Multiple services compile regex patterns on every method call instead of caching them. This adds unnecessary allocation and CPU overhead, especially when processing many tables.

## Solution

Use `[GeneratedRegex]` source generator (C# 12+) for static patterns, or cache as `private static readonly Regex` fields with `RegexOptions.Compiled`. Also fix the O(n^2) loop in Validator.ValidateModelReferences by building a lookup dictionary.
