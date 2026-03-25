# pbt - Power BI Build Tool

A semantic model composition tool for Power BI that enables version-controlled, reusable table definitions and model composition using YAML.

## Features

- **Reusable Table Definitions**: Define tables once in YAML, reference them in multiple models
- **Model Composition**: Compose Power BI models by referencing tables and defining relationships/measures
- **Advanced Model Features**: Calculation groups, perspectives, roles with RLS, field parameters
- **Lineage Tag Management**: Deterministic lineage tag generation to prevent breaking existing Power BI reports
- **Validation**: Comprehensive validation of project configuration before building
- **TMDL & PBIP Output**: Generates TMDL or full PBIP project structure for Power BI deployment
- **Pre-Build Hooks**: Run arbitrary scripts before building via `--pre-hook` (replaces built-in macros)
- **Environment Support**: Named environments for expression overrides across dev/staging/prod
- **Import/Export**: Import existing TMDL models to YAML format
- **Version Control Friendly**: All definitions in human-readable YAML files

## Installation

### Option 1: Install as Global .NET Tool (Recommended)

```bash
# Install from local package (for development)
dotnet tool install --global --add-source ./src/Pbt/nupkg Pbt

# After installation, use anywhere:
pbt --help

# Update to latest version
dotnet tool update --global Pbt

# Uninstall
dotnet tool uninstall --global Pbt
```

### Option 2: Install from NuGet (when published)

```bash
# Install from NuGet.org
dotnet tool install --global Pbt

# Use the tool
pbt --help
```

### Option 3: Build from Source

```bash
# Clone the repository
git clone https://github.com/yourusername/pbt.git
cd pbt

# Build the project
dotnet build

# Run the CLI
dotnet run --project src/Pbt -- --help
```

### Requirements

- .NET 9.0 SDK or later
- Windows, macOS, or Linux

### Verify Installation

```bash
pbt --version
pbt --help
```

## Quick Start

### 1. Initialize a New Project

```bash
pbt init my_project --examples
cd my_project
```

This creates the following structure:

```
my_project/
├── tables/              # Table definitions (reusable)
│   ├── dim_product.yaml
│   └── fact_sales.yaml
├── models/              # Model compositions (each carries its own project config)
│   └── sales_model.yaml
├── environments/            # Named environments (optional)
│   └── dev.env.yml
├── scripts/                 # Pre-build hook scripts (optional)
│   └── normalize_columns.sh
├── .pbt/                    # Tool metadata
└── target/                  # Generated output (created on build)
```

### 2. Validate Your Project

```bash
pbt validate my_project
```

### 3. Build TMDL Models

```bash
pbt build my_project
```

The TMDL output will be in `my_project/target/`.

### 4. List Tables and Models

```bash
pbt list my_project
pbt list my_project --details
```

## Project Structure

### Model Configuration (models/*.yaml)

Each model file is self-contained and carries both project-level and model-level configuration:

```yaml
name: MyModel
description: Power BI semantic model
compatibility_level: 1600  # Power BI compatibility level (default: 1600)

# Optional: explicit asset paths (defaults to tables/ and macros/ next to project root)
# assets:
#   project:
#     - path: "."
```

### Table Definitions (tables/*.yaml)

Tables are defined once and can be referenced in multiple models:

```yaml
name: Sales
description: Sales fact table
m_expression: |
  let
    Source = #table(
      {"OrderID", "CustomerID", "Amount", "OrderDate"},
      {
        {1, 100, 1500.00, #date(2024, 1, 15)},
        {2, 101, 2300.00, #date(2024, 1, 16)}
      }
    )
  in
    Source

columns:
  - name: OrderID
    type: Int64
    description: Unique order identifier

  - name: CustomerID
    type: Int64
    description: Customer reference

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
      - name: Date
        column: OrderDate
```

**Supported Data Types**: `String`, `Int64`, `DateTime`, `Decimal`, `Double`, `Boolean`

### Model Definitions (models/*.yaml)

Models reference tables and define relationships and measures:

```yaml
name: SalesAnalytics
description: Sales analytics model

tables:
  - ref: Sales
  - ref: Customers
  - ref: Products

relationships:
  - from_table: Sales
    from_column: CustomerID
    to_table: Customers
    to_column: CustomerID
    cardinality: ManyToOne
    cross_filter_direction: Both
    active: true

  - from_table: Sales
    from_column: ProductID
    to_table: Products
    to_column: ProductID
    cardinality: ManyToOne

measures:
  - name: Total Sales
    table: Sales
    expression: SUM(Sales[Amount])
    format_string: "$#,##0.00"
    display_folder: Sales Metrics

  - name: Average Order Value
    table: Sales
    expression: DIVIDE([Total Sales], DISTINCTCOUNT(Sales[OrderID]))
    format_string: "$#,##0.00"
    display_folder: Sales Metrics
```

**Cardinality Options**: `ManyToOne`, `OneToMany`, `OneToOne`, `ManyToMany`
**Cross Filter Direction**: `Single`, `Both`, `Automatic`

## Commands

### init - Initialize a New Project

```bash
pbt init <path> [--examples]
```

Creates a new pbt (Power BI Build Tool) project with the standard directory structure.

**Options:**
- `--examples`: Include example table and model files

**Example:**
```bash
pbt init my_project --examples
```

### build - Build PBIP Projects

```bash
pbt build <project-path> [options]
```

Builds a full PBIP project structure (`.pbip` + SemanticModel + Report) from YAML definitions. Use `pbt build model` for TMDL-only output.

**Options:**
- `--model <name>`: Build only the specified model
- `--output <path>`: Override output directory (default: `<project>/target`)
- `--no-lineage-tags --confirm`: Skip lineage tag generation (breaks connected reports)
- `--env <name>`: Use a named environment (loads from `environments/<name>.env.yml`)
- `--dry-run`: Validate and compose model without writing output files
- `--pre-hook <command>`: Shell command to execute before building

**Subcommands:**
- `pbt build model <project-path>`: Build TMDL-only output (no PBIP wrapper)

**Examples:**
```bash
# Build full PBIP project
pbt build my_project

# Build TMDL-only
pbt build model my_project

# Build specific model
pbt build my_project --model sales_model

# Build with environment overrides
pbt build my_project --env dev

# Dry run (validate without writing)
pbt build my_project --dry-run

# Run a pre-build script before building
pbt build my_project --pre-hook "./scripts/normalize_columns.sh"

# Build to custom output directory
pbt build my_project --output /path/to/output
```

### validate - Validate Project

```bash
pbt validate <project-path> [options]
```

Validates project configuration and definitions without building.

**Options:**
- `--verbose`: Show all validation checks performed
- `--strict`: Treat warnings as errors

**Examples:**
```bash
pbt validate my_project
pbt validate my_project --verbose
pbt validate my_project --strict
```

**What Gets Validated:**
- Project structure (models/ directory exists with at least one model file)
- Model configuration (compatibility level, name)
- Table definitions (data types, column names, hierarchies)
- Model definitions (table references, relationships, measures)
- DAX expressions (basic syntax checking)

### list - List Tables and Models

```bash
pbt list <project-path> [--details]
```

Lists all tables, models, and lineage information in the project.

**Options:**
- `--details`: Show detailed information about each table and model

**Examples:**
```bash
pbt list my_project
pbt list my_project --details
```

### import - Import TMDL or CSV to YAML

Import TMDL models or tables from different sources into YAML format.

#### import model - Import TMDL Model

```bash
pbt import model <tmdl-path> [output-path] [options]
```

Imports a complete TMDL model into YAML project structure.

**Arguments:**
- `tmdl-path`: Path to TMDL folder
- `output-path`: Path where YAML project will be created (defaults to current directory)

**Options:**
- `--include-lineage-tags`: Preserve original lineage tags from TMDL
- `--overwrite`: Overwrite existing files in output directory

**Examples:**
```bash
# Import TMDL to YAML (generates new lineage tags on next build)
pbt import model /path/to/model.tmdl my_yaml_project

# Import and preserve original lineage tags
pbt import model /path/to/model.tmdl my_yaml_project --include-lineage-tags

# Overwrite existing files
pbt import model /path/to/model.tmdl my_yaml_project --overwrite
```

#### import table - Import Tables from CSV or TMDL

```bash
pbt import table <path> [output-path] [--source-config <config>]
```

Imports table definitions from CSV schema export or TMDL model.

**Arguments:**
- `path`: Path to CSV file or TMDL directory/file
- `output-path`: Output directory for table YAML files (defaults to `./tables`)

**Options:**
- `--source-config <path>`: Source configuration file (required for CSV, ignored for TMDL)

**CSV Import:**
```bash
# Import from CSV schema export (e.g., Snowflake INFORMATION_SCHEMA)
pbt import table schema_export.csv --source-config snowflake_config.yaml

# Import to custom output directory
pbt import table schema.csv --source-config config.yaml ./custom-tables
```

**TMDL Import:**
```bash
# Import tables from TMDL model
pbt import table /path/to/model.tmdl

# Import to custom output directory
pbt import table /path/to/model.tmdl ./custom-tables
```

**Smart Merge Behavior:**

Both CSV and TMDL imports use smart merge to preserve manual edits:
- Preserves custom descriptions
- Preserves hierarchies
- Preserves manual column settings (is_hidden, format_string)
- Updates types from source (CSV/TMDL is source of truth)
- Adds new columns automatically
- Keeps removed columns by default (safer than deleting)

If a table YAML file already exists, the import will merge new changes while preserving your manual customizations.

### lineage - Manage Lineage Tags

Lineage tags are GUIDs that bind Power BI reports to model objects. pbt (Power BI Build Tool) generates deterministic tags to ensure consistency across builds.

#### lineage show - Display Lineage Manifest

```bash
pbt lineage show <project-path> [--details]
```

Shows the current lineage tag manifest.

**Options:**
- `--details`: Show all lineage tags for each object

**Example:**
```bash
pbt lineage show my_project
pbt lineage show my_project --details
```

#### lineage clean - Remove Orphaned Tags

```bash
pbt lineage clean <project-path> [--dry-run]
```

Removes lineage tags for objects that no longer exist in the project.

**Options:**
- `--dry-run`: Show what would be removed without actually removing

**Example:**
```bash
pbt lineage clean my_project
pbt lineage clean my_project --dry-run
```

#### lineage reset - Delete Lineage Manifest

```bash
pbt lineage reset <project-path> --confirm
```

Deletes the lineage manifest. The next build will generate all new lineage tags.

**⚠️ WARNING**: This will break existing Power BI reports that reference this model!

**Example:**
```bash
pbt lineage reset my_project --confirm
```

### Pre-Build Hooks

Instead of a built-in transformation DSL, pbt uses **pre-build hooks** that let you run arbitrary scripts before the build. This keeps pbt focused on model composition while giving you full control over transformations.

```bash
pbt build my_project --pre-hook "./scripts/normalize_columns.sh"
```

The hook runs in the project directory before any build steps. If the hook exits with a non-zero code, the build is aborted.

**Example scripts:**

```bash
# scripts/normalize_columns.sh - Normalize source_column names to uppercase
#!/bin/bash
for f in tables/*.yaml; do
  sed -i 's/source_column: \(.*\)/source_column: \U\1/' "$f"
done
```

```python
# scripts/validate_naming.py - Enforce naming conventions
import yaml, sys, glob
errors = []
for path in glob.glob("tables/*.yaml"):
    with open(path) as f:
        table = yaml.safe_load(f)
    for col in table.get("columns", []):
        if " " in col.get("source_column", ""):
            errors.append(f"{path}: column '{col['name']}' has spaces in source_column")
if errors:
    print("\n".join(errors), file=sys.stderr)
    sys.exit(1)
```

**Why hooks instead of built-in macros?**
- Use any language (bash, Python, PowerShell, Node.js)
- Leverage existing tools (sed, jq, yq, custom scripts)
- Full control over transformation logic
- Easy to test and debug independently
- No DSL to learn -- just shell commands

## Lineage Tag Management

pbt (Power BI Build Tool) uses **deterministic lineage tag generation** to ensure:
- Same YAML always produces the same lineage tags
- Existing Power BI reports don't break when you rebuild models
- Tags are tracked in `.pbt/lineage.yaml`

### How It Works

1. **First Build**: Generates deterministic tags based on table/column/measure names
2. **Subsequent Builds**: Reuses existing tags from the manifest
3. **New Objects**: Only new tables/columns/measures get new tags
4. **Renamed Objects**: Get new tags (since the name changed)

### Lineage Manifest Location

The lineage manifest is stored in `.pbt/lineage.yaml`:

```yaml
version: 1
generated_at: "2024-01-10T08:00:00Z"
tables:
  Sales:
    _self: a1b2c3d4-e5f6-7890-abcd-ef1234567890
    columns:
      OrderID: b2c3d4e5-f678-90ab-cdef-1234567890ab
      Amount: c3d4e5f6-7890-abcd-ef12-34567890abcd
    measures:
      Total Sales: d4e5f6a1-b2c3-4567-890a-bcdef1234567
```

### Version Control

You can choose to:
- **Track lineage tags in git** (recommended): Ensures consistent tags across team members
- **Ignore lineage tags**: Add `.pbt/lineage.yaml` to `.gitignore` if each developer should have independent tags

## Development Workflow

### Typical Workflow

```bash
# 1. Initialize project
pbt init my_project --examples
cd my_project

# 2. Edit table definitions in tables/
# 3. Create model compositions in models/

# 4. Validate before building
pbt validate .

# 5. Build TMDL
pbt build .

# 6. Deploy TMDL to Power BI
# (use Power BI Desktop or deployment tools)
```

### Iterative Development

```bash
# Make changes to YAML files

# Validate changes
pbt validate .

# Rebuild
pbt build .

# Check lineage (tags should be preserved for existing objects)
pbt lineage show .
```

### Working with Existing Models

```bash
# Import existing TMDL to YAML
pbt import model /path/to/existing_model.tmdl imported_project

# Review generated YAML
pbt list imported_project --details

# Make modifications

# Rebuild
pbt build imported_project
```

## Advanced Topics

### Reusing Tables Across Models

Define tables once in `tables/` and reference them in multiple models:

```yaml
# models/sales_model.yaml
tables:
  - ref: Sales
  - ref: Customers

# models/inventory_model.yaml
tables:
  - ref: Sales
  - ref: Products
  - ref: Inventory
```

### Environments

Named environments let you override expressions (e.g., connection strings) for dev/staging/prod:

```yaml
# environments/dev.env.yml
name: dev
expressions:
  ServerName: '"dev-server.example.com" meta [IsParameterQuery=true, Type="Text"]'
  DatabaseName: '"DevDB" meta [IsParameterQuery=true, Type="Text"]'
```

```bash
pbt build my_project --env dev
```

Expressions defined in the environment override matching expressions from `project.yml` and model definitions. You can also reference system environment variables with `${ENV_VAR}` syntax.

### Calculation Groups

Define time intelligence and other calculation groups:

```yaml
# In model definition
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

Scope visibility for different report audiences:

```yaml
perspectives:
  - name: Sales Overview
    tables:
      - Sales
      - Customers
    measures:
      - Total Sales
      - Order Count
    exclude_columns:
      - Customers.InternalID
```

### Roles with Row-Level Security

Define RLS rules declaratively:

```yaml
roles:
  - name: RegionManager
    model_permission: Read
    table_permissions:
      - table: Customers
        filter_expression: '[Country] = USERPRINCIPALNAME()'
```

### Field Parameters

Dynamic axis switching for reports:

```yaml
field_parameters:
  - name: Sales Metric
    values:
      - name: Total Sales
        expression: "NAMEOF('Sales'[Total Sales])"
        ordinal: 0
      - name: Order Count
        expression: "NAMEOF('Sales'[Order Count])"
        ordinal: 1
```

### Custom M Expressions

Tables support full Power Query M expressions for data sources:

```yaml
name: SalesFromSQL
m_expression: |
  let
    Source = Sql.Database("server", "database"),
    Sales = Source{[Schema="dbo",Item="Sales"]}[Data],
    FilteredRows = Table.SelectRows(Sales, each [Year] >= 2020)
  in
    FilteredRows
```

### Display Folders for Measures

Organize measures using display folders:

```yaml
measures:
  - name: Total Sales
    table: Sales
    expression: SUM(Sales[Amount])
    display_folder: Sales Metrics/Totals

  - name: Sales YTD
    table: Sales
    expression: TOTALYTD([Total Sales], Sales[OrderDate])
    display_folder: Sales Metrics/Time Intelligence
```

## Troubleshooting

### Validation Errors

**Error: "Unknown type 'XXX'"**
- Use only supported types: `String`, `Int64`, `DateTime`, `Decimal`, `Double`, `Boolean`

**Error: "Table 'XXX' not found in registry"**
- Ensure the table is defined in `tables/` directory
- Check that the `ref` in your model matches the table's `name` field exactly

**Error: "Column 'XXX' not found in table 'YYY'"**
- Verify the column exists in the table definition
- Check for typos in column names

**Error: "Circular relationships detected"**
- Review your relationship definitions
- Power BI does not support circular relationships

### Build Errors

**Error: "Project validation failed"**
- Run `pbt validate <project>` to see detailed errors
- Fix all validation errors before building

**Error: "TMDL serialization failed"**
- Check that all required fields are present in your YAML
- Ensure DAX expressions are syntactically valid

### Lineage Issues

**Warning: "Lineage tags changed unexpectedly"**
- This can happen if you renamed tables/columns/measures
- Check `pbt lineage show <project> --details` to see current tags
- If intentional, rebuild your Power BI reports with new tags

**Question: "Should I commit lineage.yaml to git?"**
- **Yes** if you want consistent tags across your team (recommended)
- **No** if each developer should maintain independent lineage tags

## Architecture

pbt (Power BI Build Tool) is built with:
- **.NET 9.0**: Modern C# with native performance
- **System.CommandLine**: Modern CLI framework
- **YamlDotNet**: YAML parsing and serialization
- **Microsoft.AnalysisServices.NetCore**: Power BI Tabular Object Model (TOM)

### Component Overview

- **YamlSerializer**: Handles YAML ↔ C# object conversion with snake_case mapping
- **TableRegistry**: Indexes and manages table definitions
- **ModelComposer**: Converts YAML definitions to TOM Database objects
- **Validator**: Multi-level validation of project structure and definitions
- **LineageManifestService**: Deterministic lineage tag generation and persistence

## Testing

Run the test suite:

```bash
dotnet test
```

Current test coverage: 73 tests covering:
- YAML serialization
- Table registry operations
- Model composition (including calculation groups, perspectives, roles, field parameters)
- Lineage tag management
- Validation logic
- Integration tests
- End-to-end build tests with TMDL round-trip validation

## Contributing

Contributions are welcome! Please follow these guidelines:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Write tests for your changes
4. Ensure all tests pass (`dotnet test`)
5. Commit your changes (`git commit -m 'Add amazing feature'`)
6. Push to the branch (`git push origin feature/amazing-feature`)
7. Open a Pull Request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- Built with [Microsoft Analysis Services](https://docs.microsoft.com/en-us/analysis-services/tom/introduction-to-the-tabular-object-model-tom-in-analysis-services-amo)
- Inspired by [dbt](https://www.getdbt.com/) for the composable YAML approach
- TMDL support via [Power BI Project (.pbip)](https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-overview)

## Publishing to NuGet

For maintainers: To publish a new version to NuGet.org:

```bash
# 1. Update version in src/Pbt/Pbt.csproj
# 2. Build and pack
cd src/Pbt
dotnet pack --configuration Release

# 3. Push to NuGet.org
dotnet nuget push nupkg/Pbt.X.Y.Z.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```

## Local Development

```bash
# Build the project
dotnet build

# Run tests
dotnet test

# Run the tool locally
dotnet run --project src/Pbt -- <command> <args>

# Pack as a tool
cd src/Pbt
dotnet pack

# Install locally for testing
dotnet tool install --global --add-source ./nupkg Pbt
```

## Support

- GitHub Issues: [Report bugs or request features](https://github.com/yourusername/pbt/issues)
- Documentation: See examples in `examples/sample_project/`
- Community: Join the discussion in GitHub Discussions
