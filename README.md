# pbt - Power BI Build Tool

A semantic model composition tool for Power BI that enables version-controlled, reusable table definitions and model composition using YAML.

## Features

- **Reusable Table Definitions**: Define tables once in YAML, reference them in multiple models
- **Model Composition**: Compose Power BI models by referencing tables and defining relationships/measures
- **Advanced Model Features**: Calculation groups, perspectives, roles with RLS, field parameters
- **Lineage Tag Management**: Deterministic lineage tag generation to prevent breaking existing Power BI reports
- **Validation**: Comprehensive validation of project configuration before building
- **TMDL & PBIP Output**: Generates TMDL or full PBIP project structure for Power BI deployment
- **Pre-Build Hooks**: Run arbitrary scripts before building via `--pre-hook`
- **Environment Support**: Named environments for expression overrides across dev/staging/prod
- **Import/Export**: Import existing TMDL models or CSV schemas to YAML format
- **Diff & CI**: Compare project states and detect breaking changes in CI pipelines

## Installation

```bash
dotnet tool install --global Pbt
```

Requires .NET 10.0 SDK or later. See [docs/installation.md](docs/installation.md) for alternative install methods and build-from-source instructions.

## Global Options

All commands support:

| Option | Description |
|---|---|
| `--output-format`, `-of` | Output format: `text` (default) or `json` |
| `--ci` | CI mode: no color, non-interactive, strict exit codes. Auto-detected from `CI=true` env var |

## Quick Start

```bash
# Initialize a project with example files
pbt init my_project --examples
cd my_project

# Validate definitions
pbt validate .

# Build PBIP output
pbt build .
```

Output is written to `my_project/target/`.

## Project Structure

```
my_project/
├── tables/                  # Reusable table definitions (YAML)
│   ├── dim_product.yaml
│   └── fact_sales.yaml
├── models/                  # Model compositions (YAML)
│   └── sales_model.yaml
├── environments/            # Named environments (optional)
│   └── dev.env.yml
├── scripts/                 # Pre-build hook scripts (optional)
├── .pbt/                    # Tool metadata & lineage manifest
└── target/                  # Generated output (created on build)
```

## Commands

### init

```bash
pbt init [path] [--examples]
```

Creates a new pbt project. Use `--examples` to include sample table and model files.

### build

```bash
pbt build [project-path | model-file] [options]
```

Builds a full PBIP project from YAML definitions. Pass a project directory or a model YAML file directly.

| Option | Description |
|---|---|
| `--model <name>` | Build only the specified model |
| `--output <path>` | Override output directory (default: `<project>/target`) |
| `--env <name>` | Use a named environment (`environments/<name>.env.yml`) |
| `--dry-run` | Validate and compose without writing output |
| `--pre-hook <cmd>` | Shell command to run before building (60s timeout) |
| `--no-lineage-tags` | Skip lineage tag generation (requires `--confirm`) |
| `--confirm` | Confirm destructive operations |

**Subcommand:** `pbt build model` builds TMDL-only output (no PBIP wrapper). Same options apply.

```bash
# Build all models as PBIP
pbt build my_project

# Build a single model file
pbt build models/sales_model.yaml

# Build TMDL-only with environment overrides
pbt build model my_project --env dev

# Dry run
pbt build my_project --dry-run

# Pre-build hook
pbt build my_project --pre-hook "python3 ./scripts/normalize_columns.py"
```

### validate

```bash
pbt validate [project-path | model-file] [--verbose] [--strict]
```

Validates project configuration and definitions without building. Use `--strict` to treat warnings as errors.

Checks: project structure, model configuration, table definitions, references, relationships, measures, and DAX syntax.

### list

```bash
pbt list [project-path | model-file] [--details]
```

Lists all tables, models, and lineage information. Use `--details` for column counts, descriptions, and lineage tags.

### import model

```bash
pbt import model <tmdl-path> [output-path] [options]
```

Imports a complete TMDL model into YAML project structure.

| Option | Description |
|---|---|
| `--include-lineage-tags` | Preserve original lineage tags from TMDL |
| `--overwrite` | Overwrite existing files |
| `--unsupported-objects` | Handle unsupported constructs: `warn` (default), `error`, or `skip` |
| `--show-changes` | Show diff of changes before applying |
| `--auto-merge` | Automatically merge changes without confirmation |

### import table

```bash
pbt import table <path> [output-path] [--source-config <config>]
```

Imports table definitions from CSV schema export or TMDL. Auto-detects source type by file extension.

- **CSV**: Requires `--source-config` pointing to a connector config file (supports `snowflake`, `sqlserver`)
- **TMDL**: Optionally pass `--include-lineage-tags`

Both modes use **smart merge**: new columns are added, types are updated from source, and manual edits (descriptions, hierarchies, formatting) are preserved.

### lineage

Manage the lineage tag manifest (`.pbt/lineage.yaml`).

```bash
# Show manifest summary (add --details for all tags)
pbt lineage show [project-path] [--details]

# Remove tags for deleted objects (add --dry-run to preview)
pbt lineage clean [project-path] [--dry-run]

# Delete manifest entirely (requires --confirm; breaks existing reports)
pbt lineage reset [project-path] --confirm
```

### diff

```bash
pbt diff <path-a> <path-b> [--breaking] [--output text|json]
```

Compares two project states and classifies each change as breaking or non-breaking.

**Breaking**: table/column/measure/relationship/model removed, column type changed.
**Non-breaking**: additions, description/format/expression changes.

Use `--breaking` in CI to fail on breaking changes.

## YAML Reference

### Table Definition (`tables/*.yaml`)

```yaml
name: Sales
description: Sales fact table
m_expression: |
  let
    Source = Sql.Database("server", "database"),
    Sales = Source{[Schema="dbo",Item="Sales"]}[Data]
  in
    Sales

columns:
  - name: OrderID
    type: Int64
    description: Unique order identifier
  - name: Amount
    type: Decimal
    format_string: "$#,##0.00"
  - name: OrderDate
    type: DateTime

hierarchies:
  - name: Date Hierarchy
    levels:
      - name: Year
        column: Year
      - name: Month
        column: Month
```

**Supported types**: `String`, `Int64`, `DateTime`, `Decimal`, `Double`, `Boolean`

### Model Definition (`models/*.yaml`)

```yaml
name: SalesAnalytics
description: Sales analytics model
compatibility_level: 1600

tables:
  - ref: Sales
  - ref: Customers

relationships:
  - from_table: Sales
    from_column: CustomerID
    to_table: Customers
    to_column: CustomerID
    cardinality: ManyToOne        # ManyToOne | OneToMany | OneToOne | ManyToMany
    cross_filter_direction: Both  # Single | Both | Automatic
    active: true

measures:
  - name: Total Sales
    table: Sales
    expression: SUM(Sales[Amount])
    format_string: "$#,##0.00"
    display_folder: Sales Metrics
```

### Environments (`environments/<name>.env.yml`)

Override expressions per environment. Reference system env vars with `${ENV_VAR}`.

```yaml
name: dev
expressions:
  ServerName: '"dev-server.example.com" meta [IsParameterQuery=true, Type="Text"]'
  DatabaseName: '"DevDB" meta [IsParameterQuery=true, Type="Text"]'
```

```bash
pbt build my_project --env dev
```

### Calculation Groups

```yaml
calculation_groups:
  - name: Time Intelligence
    precedence: 10
    columns:
      - name: Time Calculation
        type: String
        source_column: Name
    calculation_items:
      - name: Current
        expression: SELECTEDMEASURE()
        ordinal: 0
      - name: YTD
        expression: CALCULATE(SELECTEDMEASURE(), DATESYTD('Date'[Date]))
        ordinal: 1
```

### Perspectives

```yaml
perspectives:
  - name: Sales Overview
    tables:
      - Sales
      - Customers
    measures:
      - Total Sales
    exclude_columns:
      - Customers.InternalID
```

### Roles (RLS)

```yaml
roles:
  - name: RegionManager
    model_permission: Read
    table_permissions:
      - table: Customers
        filter_expression: '[Country] = USERPRINCIPALNAME()'
```

### Field Parameters

```yaml
field_parameters:
  - name: Sales Metric
    values:
      - name: Total Sales
        expression: "NAMEOF('Sales'[Total Sales])"
        ordinal: 0
```

## Lineage Tags

pbt generates **deterministic lineage tags** so that rebuilding models doesn't break connected Power BI reports. Tags are stored in `.pbt/lineage.yaml`.

- **First build**: generates tags based on object names
- **Subsequent builds**: reuses existing tags
- **New objects**: get new tags; renamed objects get new tags
- **Recommended**: commit `lineage.yaml` to git for team consistency

## Pre-Build Hooks

Run arbitrary scripts before building. The hook runs in the project directory; a non-zero exit aborts the build.

```bash
pbt build my_project --pre-hook "./scripts/normalize_columns.sh"
```

Use any language (bash, Python, PowerShell, Node.js) — no DSL to learn.

## Development

```bash
dotnet build                                  # Build
dotnet test                                   # Run tests (79 tests)
dotnet run --project src/Pbt -- <command>     # Run locally
```

## Troubleshooting

| Error | Fix |
|---|---|
| Unknown type 'XXX' | Use: `String`, `Int64`, `DateTime`, `Decimal`, `Double`, `Boolean` |
| Table 'XXX' not found | Ensure table exists in `tables/` and `ref` matches the `name` field |
| Column 'XXX' not found in table 'YYY' | Check for typos in column names |
| Circular relationships detected | Review relationship definitions; Power BI doesn't support cycles |
| Project validation failed | Run `pbt validate <project>` for details |

## Contributing

1. Fork the repository
2. Create a feature branch
3. Write tests and ensure `dotnet test` passes
4. Open a Pull Request

## License

MIT License - see the LICENSE file for details.

## Links

- [Installation & publishing](docs/installation.md)
- [Microsoft Analysis Services TOM](https://docs.microsoft.com/en-us/analysis-services/tom/introduction-to-the-tabular-object-model-tom-in-analysis-services-amo)
- [Power BI PBIP](https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-overview)
- Inspired by [dbt](https://www.getdbt.com/)
