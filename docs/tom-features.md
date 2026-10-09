# TOM feature coverage (YAML keys)

Every definition below also accepts `annotations` and `extended_properties` (string maps).
Features need a compatibility level that supports them; the build fails with a TOM error otherwise.

| Object | Keys |
|---|---|
| Model | `data_sources`, `functions`, `cultures`, `expressions` |
| Table | `calculated_expression`, `data_category`, `is_private`, `exclude_from_model_refresh`, `detail_rows_expression`, `alternate_source_precedence` |
| Partition | `description`, `mode` (any TOM mode), `m_expression`, `calculated_expression`, `query` + `data_source`, `entity_name` + `schema_name` + `expression_source` (Direct Lake) |
| Column | `is_nullable`, `is_unique`, `encoding_hint`, `alternate_of: {summarization, base_column \| base_table}` (aggregations) |
| Measure | `format_string_expression`, `detail_rows_expression`, `kpi` |
| Relationship | `security_filtering_behavior`, `join_on_date_behavior` |
| Role table permission | `column_permissions: {Column: None\|Read}` (OLS) |
| Calculation group | `no_selection_expression`, `multiple_or_empty_selection_expression` |

```yaml
data_sources:
  - name: Sql
    protocol: tds
    address: { server: myserver, database: mydb }
cultures:
  - name: sv-SE
    translations:
      - { table: Sales, object: Amount, property: Caption, value: Belopp }
```

`pbt import` reads all of the above back into YAML (except Calendars). `pbt diff` composes both projects
and compares the TOM databases property by property.
