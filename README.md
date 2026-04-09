# pbt — Power BI Build Tool

pbt lets you define semantic model components once in YAML and compose them into multiple valid PBIP/TMDL projects — without writing raw TMDL or opening Power BI Desktop. Tables, measures, relationships, and configuration are authored in plain text, then assembled into standard TMDL/PBIP output that Power BI consumes natively. Because everything is YAML, your models live in version control and can be shared, reviewed, and updated like any other code.

## Why pbt?

- **Define once, reuse everywhere** — a table defined in `tables/sales.yaml` can appear in any number of models without duplication
- **No manual TMDL editing** — changes to YAML propagate automatically on the next build
- **Environment-aware** — swap connection strings and parameters between dev, staging, and prod with named environments
- **Safe refactoring** — lineage tags are generated deterministically so existing Power BI reports stay connected after rebuilds
- **Bring your own tooling** — pre-build hooks let you run any script (Python, bash, PowerShell) before the build
- **Version-control friendly** — plain text YAML works naturally with git, pull requests, and code review

## Installation

**Requirements**: .NET 10.0 SDK or later

```bash
# Install as a global .NET tool (recommended)
dotnet tool install --global --add-source ./src/Pbt/nupkg Pbt

# Verify
pbt --version
```

<details>
<summary>Other installation options</summary>

```bash
# Install from NuGet (when published)
dotnet tool install --global Pbt

# Run from source
git clone https://github.com/jonaolden/pbt.git
cd pbt
dotnet run --project src/Pbt -- --help
```

</details>

## Quick Start

```bash
# 1. Create a new project with example files
pbt init my_project --examples
cd my_project

# 2. Validate your definitions
pbt validate .

# 3. Build PBIP output
pbt build .

# 4. Inspect what was created
pbt list . --details
```

Output lands in `my_project/target/` as a fully valid `.pbip` project ready for Power BI Desktop or deployment.

## Project Layout

```
my_project/
├── tables/          # Reusable table definitions (YAML)
├── models/          # Model compositions — each references tables and defines relationships/measures
├── environments/    # Named environments for dev/staging/prod expression overrides
├── scripts/         # Optional pre-build hook scripts
├── .pbt/            # Tool metadata (lineage manifest)
└── target/          # Generated PBIP/TMDL output (created on build)
```

Tables live independently of models so the same table can be composed into multiple models without duplication:

```yaml
# models/sales_model.yaml
tables:
  - ref: Sales
  - ref: Customers

# models/inventory_model.yaml
tables:
  - ref: Sales       # same definition, no copy needed
  - ref: Products
```

## Commands

| Command | What it does |
|---------|-------------|
| `pbt init <path> [--examples]` | Scaffold a new project |
| `pbt build <path> [--env <name>] [--dry-run]` | Compile YAML → PBIP/TMDL output |
| `pbt build model <path>` | TMDL-only output (no PBIP wrapper) |
| `pbt validate <path> [--strict]` | Check definitions without building |
| `pbt list <path> [--details]` | Show tables, models, and lineage |
| `pbt import model <tmdl-path>` | Import an existing TMDL model to YAML |
| `pbt import table <path>` | Import tables from TMDL or CSV |
| `pbt lineage show/clean/reset <path>` | Manage lineage tag manifest |
| `pbt diff <path-a> <path-b> [--breaking]` | Detect breaking schema changes |

Run `pbt <command> --help` for full option details.

## Examples

The `examples/sample_project/` directory contains a complete working project demonstrating:

- Fact and dimension table definitions with multiple partitions
- Multi-table model composition with relationships
- DAX measures and calculation groups
- Time intelligence, perspectives, and RLS roles
- Dev/prod environment overrides
- Pre-build hook scripts

## Contributing

Contributions are welcome. Please open an issue or pull request on [GitHub](https://github.com/jonaolden/pbt/issues).

1. Fork the repository and create a feature branch
2. Write tests for your changes
3. Run `dotnet test` and ensure all tests pass
4. Open a pull request

## License

MIT — see [LICENSE](LICENSE).

---

*Inspired by [dbt](https://www.getdbt.com/). Built on [Microsoft Analysis Services TOM](https://docs.microsoft.com/en-us/analysis-services/tom/introduction-to-the-tabular-object-model-tom-in-analysis-services-amo) and [Power BI PBIP](https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-overview).*
