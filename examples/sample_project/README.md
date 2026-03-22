# Sample PBI Composer Project

This is an example project demonstrating the structure and usage of PBI Composer.

## Project Structure

```
sample_project/
├── project.yml          # Project configuration
├── tables/              # Table definitions (reusable)
│   ├── sales.yaml
│   └── customers.yaml
├── models/              # Model compositions
│   └── sales_model.yaml
├── target/              # Generated TMDL output (created by build)
└── .pbicomposer/        # Lineage tag manifest (auto-generated)
```

## Files

### project.yml
Defines project-level configuration:
- Project name
- Compatibility level (Power BI dataset compatibility)

### tables/
Contains reusable table definitions:
- `sales.yaml` - Sales fact table with M expression and columns
- `customers.yaml` - Customer dimension table

Each table file includes:
- M expression (Power Query code for data loading)
- Column definitions with types and metadata
- Optional hierarchies

### models/
Contains model compositions:
- `sales_model.yaml` - Combines Sales and Customers tables

Model files define:
- Which tables to include (by reference)
- Relationships between tables
- Measures (DAX expressions)

## Usage

```bash
# Validate the project
pbicomposer validate ./sample_project

# Build TMDL from YAML
pbicomposer build ./sample_project

# List tables and models
pbicomposer list ./sample_project

# View lineage tags
pbicomposer lineage show ./sample_project
```

## Generated Output

After running `pbicomposer build`, the `target/` directory will contain:
```
target/
└── SalesAnalytics/
    ├── database.tmdl
    ├── model.tmdl
    └── tables/
        ├── Sales.tmdl
        └── Customers.tmdl
```

This TMDL can be opened in Power BI Desktop or deployed to Power BI Service.
