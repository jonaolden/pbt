# Sample PBI Composer Project

This is an example project demonstrating the structure and usage of PBI Composer.

## Project Structure

```
sample_project/
├── tables/              # Table definitions (reusable)
│   ├── sales.yaml
│   └── customers.yaml
├── models/              # Model compositions
│   └── sales_model.yaml
├── target/              # Generated TMDL output (created by build)
└── .pbt/                # Lineage tag manifest (auto-generated)
```

## Files

### models/sales_model.yaml
Each model file is self-contained and carries all project-level configuration:
- `name` / `description` — semantic model name and description
- `compatibility_level` — Power BI dataset compatibility level
- `format_strings` — optional type-level default format strings
- `assets` — optional override for table/macro lookup paths (defaults to `tables/` and `macros/` next to the project root)
- `builds` — optional build output path override
- Tables, relationships, measures, calculation groups, etc.

### tables/
Contains reusable table definitions:
- `sales.yaml` - Sales fact table with M expression and columns
- `customers.yaml` - Customer dimension table

Each table file includes:
- M expression (Power Query code for data loading)
- Column definitions with types and metadata
- Optional hierarchies

## Usage

```bash
# Validate the project
pbt validate ./sample_project

# Build Power BI project (.pbip) from YAML
pbt build ./sample_project

# List tables and models
pbt list ./sample_project

# View lineage tags
pbt lineage show ./sample_project
```

## Generated Output

After running `pbt build`, the `target/` directory will contain:
```
target/
└── SalesAnalytics.pbip
└── SalesAnalytics.SemanticModel/
    └── definition/
        ├── database.tmdl
        ├── model.tmdl
        └── tables/
            ├── Sales.tmdl
            └── Customers.tmdl
```

This PBIP can be opened in Power BI Desktop or deployed to Power BI Service.
