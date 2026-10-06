# csv-to-tmdl

pbt plugin. Reads an `INFORMATION_SCHEMA.COLUMNS`-style CSV export and writes one
Power BI table `.tmdl` per table. Stdlib only.

```bash
pip install -e plugins/csv_to_tmdl
csv-to-tmdl schema.csv --source-type sqlserver -o tables_tmdl/
pbt import table tables_tmdl/ ./tables      # TMDL -> pbt YAML
```

CSV headers (case-insensitive): required `table_name`, `column_name`, `data_type`;
optional `ordinal_position`, `table_comment`, `column_comment`. Other columns are ignored.

`--source-type` (`snowflake` default, `sqlserver`) selects the type map in
`csv_to_tmdl/types.py`; unknown types become `string`. Output has columns only, no
partition: add `source:` in the pbt YAML after import.

Tests: `cd plugins/csv_to_tmdl && python -m pytest tests/`
