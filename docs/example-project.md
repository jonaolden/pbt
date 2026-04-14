# Example Project Walkthrough

The `examples/sample_project/` directory is a complete, buildable pbt project. It demonstrates every major feature using a simple sales analytics scenario: three tables (Sales, Customers, DateDim) composed into one model with relationships, measures, RLS roles, time intelligence, and more.

## Project Layout

```
examples/sample_project/
├── tables/
│   ├── sales.yaml           # Fact table with multiple partitions and a calculated column
│   ├── customers.yaml       # Dimension table with a hierarchy and data categories
│   └── datedim.yaml         # Date dimension with sort-by-column
├── models/
│   └── sales_model.yaml     # Model composition: relationships, measures, and advanced features
├── environments/
│   └── dev.env.yml          # Dev overrides for connection string expressions
├── scripts/
│   ├── normalize_columns.py # Pre-build hook: rename DIM_ tables and title-case columns
│   └── validate_naming.py   # Pre-build hook: enforce naming conventions as a gate
├── .pbt/
│   └── lineage.yaml         # Auto-generated lineage tag manifest
└── target/                  # Built PBIP output (created by pbt build)
```

## Running the Example

```bash
cd examples/sample_project

# Validate all definitions
pbt validate .

# Build a full PBIP project
pbt build .

# Or build TMDL-only
pbt build model .

# Inspect tables and models
pbt list . --details

# View the generated lineage manifest
pbt lineage show . --details
```

After building, `target/` will contain a `SalesAnalytics.pbip` file and accompanying SemanticModel/Report folders that can be opened directly in Power BI Desktop.

---

## tables/sales.yaml — Fact Table

```yaml
name: Sales
description: Sales fact table with multiple partitions
partitions:
  - name: Sales_Historical
    mode: Import
    m_expression: |
      let
        Source = #table(
          {"OrderID", "OrderDate", "Amount", "CustomerID", "DateKey"},
          { {1, #date(2024, 1, 15), 1500.00, 101, 20240115}, ... }
        )
      in Source
  - name: Sales_Current
    mode: Import
    m_expression: |
      ...
```

**Features demonstrated:**

- **Multiple partitions** — `Sales_Historical` and `Sales_Current` are separate partitions of the same table. Each has its own M expression. This pattern is common when partitioning by time range.
- **`is_key`** — `OrderID` is marked as the primary key (`is_key: true`). pbt uses this to set the `isKey` property on the column in TMDL.
- **`is_hidden`** — `DateKey` is hidden (`is_hidden: true`). It exists only to join to the date dimension; there is no reason to expose it in reports.
- **`summarize_by`** — Key and ID columns use `summarize_by: None` to prevent Power BI from auto-aggregating them.
- **Calculated column** — `IsLargeOrder` has a DAX `expression` instead of a `source_column`. pbt emits this as a calculated column in TMDL.
- **`display_folder`** — The `IsLargeOrder` column is grouped under `Flags` in the Power BI field list.
- **Table-level measures** — `Total Sales` and `Number of Orders` are defined directly on the table. Model-level measures can reference them with `[Total Sales]`.

---

## tables/customers.yaml — Dimension Table

```yaml
name: Customers
description: Customer dimension table with geography hierarchy
columns:
  - name: CustomerName
    type: String
    source_column: CustomerName
    data_category: Organization
  - name: City
    type: String
    source_column: City
    data_category: City
  - name: Country
    type: String
    source_column: Country
    data_category: Country
    annotations:
      PBI_GeoEncoding: Country
hierarchies:
  - name: Geography
    display_folder: Geo
    levels:
      - name: Region
        column: Region
      - name: Country
        column: Country
      - name: City
        column: City
```

**Features demonstrated:**

- **`data_category`** — `City`, `Country`, and `Organization` tell Power BI how to interpret columns for map visuals and Bing geocoding. Valid values mirror the Power BI data category options.
- **`annotations`** — `PBI_GeoEncoding: Country` is a raw Power BI annotation passed through as-is to the TMDL output. Useful for features pbt doesn't model explicitly.
- **Hierarchy** — The `Geography` hierarchy (`Region > Country > City`) lets report users drill down through geography in a single field. `display_folder` places the hierarchy in the `Geo` folder.

---

## tables/datedim.yaml — Date Dimension

```yaml
name: DateDim
columns:
  - name: MonthNum
    type: Int64
    source_column: MonthNum
    is_hidden: true
    summarize_by: None
  - name: MonthName
    type: String
    source_column: MonthName
    sort_by_column: MonthNum
```

**Features demonstrated:**

- **`sort_by_column`** — `MonthName` (a text column like "January") is sorted by `MonthNum` (an integer). Without this, Power BI would sort month names alphabetically (April, August, ...). pbt emits the `sortByColumn` property in TMDL.
- **Hidden helper column** — `MonthNum` is hidden so report users only see `MonthName`, but the sort still works.

---

## models/sales_model.yaml — Model Composition

This file wires together the three tables and adds all the model-level features.

### Tables and Relationships

```yaml
tables:
  - ref: Sales
  - ref: Customers
  - ref: DateDim

relationships:
  - from_table: Sales
    from_column: CustomerID
    to_table: Customers
    to_column: CustomerID
    cardinality: ManyToOne
    cross_filter_direction: Both
    active: true
    rely_on_referential_integrity: true

  - from_table: Sales
    from_column: DateKey
    to_table: DateDim
    to_column: DateKey
    cardinality: ManyToOne
    cross_filter_direction: Single
    active: true
```

`ref: Sales` pulls in `tables/sales.yaml` by the `name` field. The same `sales.yaml` could be referenced in any other model without copying.

`rely_on_referential_integrity: true` on the Sales→Customers relationship tells Power BI it can use an inner join, which can improve query performance.

### Model-Level Measures

```yaml
measures:
  - name: Average Order Value
    table: Sales
    expression: DIVIDE([Total Sales], [Number of Orders])
    format_string: "$#,##0.00"
    display_folder: Sales Metrics
```

`[Total Sales]` and `[Number of Orders]` are defined in `sales.yaml`. The model YAML adds `Average Order Value` on top without modifying the table file.

### Expressions (M Parameters)

```yaml
expressions:
  - name: DatabaseName
    kind: M
    expression: '"SalesDB" meta [IsParameterQuery=true, Type="Text", IsParameterQueryRequired=true]'
    description: Target database name
```

Shared M parameters appear in `target/.../expressions.tmdl` and can be referenced by M expressions in partitions. The `environments/dev.env.yml` file overrides `DatabaseName` to point at the dev database.

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
        expression: CALCULATE(SELECTEDMEASURE(), DATESYTD(DateDim[DateKey]))
        ordinal: 1
      - name: PY
        expression: CALCULATE(SELECTEDMEASURE(), SAMEPERIODLASTYEAR(DateDim[DateKey]))
        ordinal: 2
```

Calculation groups let report users apply time intelligence to any measure without modifying the model. The `precedence` value controls evaluation order when multiple calculation groups interact.

### Perspectives

```yaml
perspectives:
  - name: Sales Overview
    description: High-level sales view for executives
    tables:
      - Sales
      - Customers
    measures:
      - Total Sales
      - Number of Orders
      - Average Order Value
```

Perspectives scope what report authors see in the field list. `Sales Overview` exposes only the two key tables and three measures, hiding the date dimension and detailed columns from this view.

### Roles (Row-Level Security)

```yaml
roles:
  - name: RegionManager
    model_permission: Read
    table_permissions:
      - table: Customers
        filter_expression: '[Country] = "USA"'
```

The `RegionManager` role restricts `Customers` rows to the USA. Any report consuming this model with the `RegionManager` role active will only show USA customers. In production you would replace `"USA"` with `USERPRINCIPALNAME()` or a security table lookup.

### Field Parameters

```yaml
field_parameters:
  - name: Sales Metric
    values:
      - name: Total Sales
        expression: NAMEOF('Sales'[Total Sales])
        ordinal: 0
      - name: Number of Orders
        expression: NAMEOF('Sales'[Number of Orders])
        ordinal: 1
```

Field parameters let report users switch between metrics on a chart axis without building separate visuals. pbt generates the required calculated table and columns in TMDL.

---

## environments/dev.env.yml — Environment Overrides

```yaml
name: dev
description: Development environment overrides
expressions:
  ServerName: '"dev-server.example.com" meta [IsParameterQuery=true, Type="Text", IsParameterQueryRequired=true]'
  DatabaseName: '"DevSalesDB" meta [IsParameterQuery=true, Type="Text", IsParameterQueryRequired=true]'
```

Build with this environment:

```bash
pbt build . --env dev
```

`ServerName` and `DatabaseName` override the expressions defined in `models/sales_model.yaml`. The override value must be a complete M expression including the `meta` block — pbt replaces the expression verbatim.

You can also reference OS environment variables in expression values using `${VAR_NAME}` syntax:

```yaml
expressions:
  ServerName: '"${DB_SERVER}" meta [IsParameterQuery=true, Type="Text"]'
```

---

## scripts/ — Pre-Build Hooks

Pre-build hooks are arbitrary scripts passed to `pbt build --pre-hook`. They run in the project directory before any build steps; a non-zero exit code aborts the build.

### normalize_columns.py

Transforms raw `DIM_*` table names and `UPPER_SNAKE_CASE` column names into Power BI display conventions:

| Before | After |
|--------|-------|
| Table `DIM_BUSINESS_SEGMENT` | `Business Segment` |
| Column `BUSINESS_SEGMENT_NAME` | `Business Segment Name` |
| Column `BUSINESS_SEGMENT_ID` | `BUSINESS_SEGMENT_ID` + `is_hidden: true` |

`source_column` values are never modified — they must match the data source exactly.

```bash
pbt build . --pre-hook "python3 ./scripts/normalize_columns.py"
```

### validate_naming.py

A CI gate that fails the build if:
- Any table still has a `DIM_` prefix (normalize should have run first)
- Any `source_column` value contains spaces
- Any `_ID` column is not hidden

```bash
pbt build . --pre-hook "python3 ./scripts/validate_naming.py"
```

Run validation after normalization to confirm the transform was applied correctly.

---

## Generated Output

After `pbt build .`, the `target/` directory contains a complete PBIP project:

```
target/
├── SalesAnalytics.pbip
├── SalesAnalytics.SemanticModel/
│   ├── .platform
│   ├── definition.pbism
│   └── definition/
│       ├── database.tmdl
│       ├── model.tmdl
│       ├── expressions.tmdl
│       ├── relationships.tmdl
│       ├── tables/
│       │   ├── Sales.tmdl
│       │   ├── Customers.tmdl
│       │   ├── DateDim.tmdl
│       │   ├── Time Intelligence.tmdl   ← calculation group
│       │   └── Sales Metric.tmdl        ← field parameter
│       ├── perspectives/
│       │   └── Sales Overview.tmdl
│       └── roles/
│           └── RegionManager.tmdl
└── SalesAnalytics.Report/
    ├── .platform
    ├── definition.pbir
    └── definition/
        ├── report.json
        └── pages/
```

Open `SalesAnalytics.pbip` in Power BI Desktop (version that supports PBIP) to connect to the semantic model and start building reports. The `.pbip` file and TMDL folders can also be deployed to Power BI Service using the REST API or Power BI deployment pipelines.
