# pbt - Power BI Build Tool

A semantic model composition tool for Power BI that enables version-controlled, reusable table definitions and model composition using YAML.

## Installation

```bash
dotnet tool install --global Pbt
```

Requires .NET 10.0 SDK or later. See [docs/installation.md](docs/installation.md) for alternative install methods and build-from-source instructions.

## Quick Start

```bash
pbt init my_project --examples
cd my_project
pbt validate .
pbt build .
```

Output is written to `my_project/target/`.

## Project Structure

```
my_project/
├── tables/                  # Reusable table definitions (YAML)
├── models/                  # Model compositions (YAML)
├── environments/            # Named environments (optional)
├── scripts/                 # Pre-build hook scripts (optional)
├── .pbt/                    # Tool metadata & lineage manifest
└── target/                  # Generated output (created on build)
```

## Global Options

All commands support:

| Option | Description |
|---|---|
| `--output-format`, `-of` | Output format: `text` (default) or `json` |
| `--ci` | CI mode: no color, non-interactive, strict exit codes. Auto-detected from `CI=true` env var |

## Commands

### init

```bash
pbt init [path] [--examples]
```

Creates a new pbt project with the standard directory structure. Use `--examples` to include sample table and model files.

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
pbt build my_project                        # Build all models as PBIP
pbt build models/sales_model.yaml           # Build a single model file
pbt build model my_project --env dev        # TMDL-only with environment
pbt build my_project --dry-run              # Validate without writing
pbt build my_project --pre-hook "./scripts/normalize_columns.sh"
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

Imports a complete TMDL model into a YAML project structure (tables, model definition, `.gitignore`).

| Option | Description |
|---|---|
| `--include-lineage-tags` | Preserve original lineage tags from TMDL |
| `--overwrite` | Overwrite existing files in output directory |
| `--unsupported-objects` | Handle unsupported constructs: `warn` (default), `error`, or `skip` |
| `--show-changes` | Show diff of changes before applying |
| `--auto-merge` | Automatically merge changes without confirmation |

Unsupported constructs detected: calculation groups, perspectives, roles, translations/cultures, KPIs.

### import table

```bash
pbt import table <path> [output-path] [--source-config <config>] [--include-lineage-tags]
```

Imports table definitions from a **CSV schema export** or **TMDL model**. Auto-detects source type by file extension.

Both modes use **smart merge** when a table YAML already exists: new columns are added, types are updated from source, and manual edits (descriptions, hierarchies, formatting) are preserved.

#### From TMDL

```bash
pbt import table /path/to/model.tmdl ./tables
pbt import table /path/to/model.tmdl ./tables --include-lineage-tags
```

Extracts individual table definitions from a TMDL model.

#### From CSV (Snowflake, SQL Server)

```bash
pbt import table schema_export.csv --source-config snowflake_config.yaml
pbt import table schema_export.csv --source-config config.yaml ./custom-tables
```

Imports tables from a CSV schema export (e.g., Snowflake `INFORMATION_SCHEMA` query). Requires `--source-config` pointing to a connector config file.

**CSV columns** (matches `INFORMATION_SCHEMA` output):

| Column | Required | Description |
|---|---|---|
| `table_name` | yes | Table name |
| `column_name` | yes | Column name |
| `data_type` | yes | Database-native type (e.g., `VARCHAR`, `NUMBER`, `TIMESTAMP_NTZ`) |
| `table_catalog` | no | Database name |
| `table_schema` | no | Schema name |
| `ordinal_position` | no | Column ordering |
| `is_nullable` | no | Nullable flag |
| `column_default` | no | Default value |
| `column_comment` | no | Column description |
| `table_comment` | no | Table description |

**Source config file** (`snowflake_config.yaml`):

```yaml
source_type: snowflake          # snowflake | sqlserver

connector:
  name: SnowflakeSource         # Expression name in TMDL
  connection: myaccount.snowflakecomputing.com
  warehouse: ANALYTICS_WH       # Required for Snowflake

datatypes:
  mappings:
    - database_type: VARCHAR
      m_type: Text.Type
      tmdl_type: string
    - database_type: NUMBER
      m_type: Int64.Type
      tmdl_type: int64
    - database_type: TIMESTAMP_NTZ
      m_type: DateTime.Type
      tmdl_type: dateTime
  regex_overrides:
    - pattern: ["_ID$", "^DW_"]
      m_type: Int64.Type
      tmdl_type: int64

column_naming:
  conversion: pascalcase        # pascalcase | camelcase | snake_case | none
  preserve_patterns: ["^DW_"]   # Skip conversion for matching columns
  rules:
    - pattern: "^DW_"
      is_hidden: true
      description: "System column"
  groups:                        # Table-specific overrides (first match wins)
    - table_pattern: "^FACT_"
      table_is_hidden: false
      table_name_conversion: pascalcase
      conversion: pascalcase
    - table_pattern: "^STG_"
      table_is_hidden: true
      table_name_conversion: none
```

### lineage

Manage the lineage tag manifest (`.pbt/lineage.yaml`). Lineage tags are deterministic GUIDs that bind Power BI reports to model objects — rebuilding a model preserves tags so connected reports don't break.

```bash
pbt lineage show [project-path] [--details]       # Show manifest
pbt lineage clean [project-path] [--dry-run]       # Remove orphaned tags
pbt lineage reset [project-path] --confirm         # Delete manifest (breaks reports)
```

Recommended: commit `.pbt/lineage.yaml` to git for team consistency.

### diff

```bash
pbt diff <path-a> <path-b> [--breaking] [--output text|json]
```

Compares two project states and classifies each change as breaking or non-breaking.

**Breaking**: table/column/measure/relationship/model removed, column type changed.
**Non-breaking**: additions, description/format/expression changes.

Use `--breaking` in CI to return a non-zero exit code on breaking changes.

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

perspectives:
  - name: Sales Overview
    tables: [Sales, Customers]
    measures: [Total Sales]
    exclude_columns: [Customers.InternalID]

roles:
  - name: RegionManager
    model_permission: Read
    table_permissions:
      - table: Customers
        filter_expression: '[Country] = USERPRINCIPALNAME()'

field_parameters:
  - name: Sales Metric
    values:
      - name: Total Sales
        expression: "NAMEOF('Sales'[Total Sales])"
        ordinal: 0
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
