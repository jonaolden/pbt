# CLI Reference

All commands accept `--help` for inline option descriptions.

```
pbt <command> [subcommand] [arguments] [options]
```

---

## init

Initialize a new pbt project with the standard directory structure.

```
pbt init <path> [--examples]
```

**Arguments**

| Argument | Description |
|----------|-------------|
| `path` | Directory to create. Created if it does not exist. |

**Options**

| Option | Description |
|--------|-------------|
| `--examples` | Populate `tables/` and `models/` with starter YAML files. |

**What gets created**

```
<path>/
├── tables/        # Table definitions
├── models/        # Model compositions
├── environments/  # Named environment files (optional)
├── scripts/       # Pre-build hook scripts (optional)
└── .pbt/          # Tool metadata
```

With `--examples`, stub table and model files are added so you can run `pbt build` immediately.

**Examples**

```bash
pbt init my_project
pbt init my_project --examples
```

---

## build

Compile YAML definitions into PBIP or TMDL output.

### pbt build

Produces a full PBIP project (`.pbip` file + SemanticModel + Report folders).

```
pbt build [<project-path>] [options]
```

**Arguments**

| Argument | Default | Description |
|----------|---------|-------------|
| `project-path` | `.` | Path to the project directory **or** a model YAML file. When a file path is given, the project root is inferred from the file's location and only that model is built. |

**Options**

| Option | Description |
|--------|-------------|
| `--model <name>` | Build only the named model (when `project-path` is a directory). |
| `--output <path>` | Override the output directory. Default: `<project>/target`. |
| `--env <name>` | Load `environments/<name>.env.yml` and apply its expression overrides. |
| `--dry-run` | Validate and compose the model without writing any files. |
| `--pre-hook <command>` | Shell command to run in the project directory before building. A non-zero exit code aborts the build. Has a 60-second timeout. |
| `--no-lineage-tags` | Skip lineage tag generation. **Requires `--confirm`.** Warning: breaks all Power BI reports connected to this model. |
| `--confirm` | Required companion to `--no-lineage-tags`. |

**Examples**

```bash
# Build all models in the current directory
pbt build .

# Build a single model by file path
pbt build models/sales_model.yaml

# Build a specific model by name
pbt build my_project --model sales_model

# Apply dev environment overrides
pbt build my_project --env dev

# Validate without writing files
pbt build my_project --dry-run

# Run a pre-build script
pbt build my_project --pre-hook "python3 ./scripts/normalize_columns.py"

# Custom output directory
pbt build my_project --output /tmp/pbip_output
```

### pbt build model

Produces TMDL-only output (no `.pbip` wrapper, no Report folder). Useful when deploying directly via the Power BI REST API or when you only need the semantic model definition.

```
pbt build model [<project-path>] [options]
```

Accepts the same options as `pbt build`.

```bash
pbt build model .
pbt build model my_project --env prod
```

---

## validate

Check project definitions without building. Reads all model and table files, resolves references, and reports errors and warnings.

```
pbt validate [<project-path>] [options]
```

**Arguments**

| Argument | Default | Description |
|----------|---------|-------------|
| `project-path` | `.` | Project directory or model YAML file. |

**Options**

| Option | Description |
|--------|-------------|
| `--verbose` | Print each validation check as it runs. |
| `--strict` | Treat warnings as errors (exits non-zero if any warnings are found). |

**What gets validated**

- Project structure (`models/` directory exists and contains at least one `.yaml` file)
- Model configuration (name, compatibility level)
- Table definitions (column data types, hierarchy column references)
- Model references (every `ref:` matches a table in `tables/`)
- Relationships (column names exist on both sides)
- Measures (basic DAX syntax)

**Examples**

```bash
pbt validate .
pbt validate my_project --verbose
pbt validate my_project --strict   # use in CI to catch warnings
```

**Exit codes**: `0` = valid, `1` = errors (or warnings in strict mode).

---

## list

Show all tables and models in a project, plus a lineage summary.

```
pbt list [<project-path>] [--details]
```

**Arguments**

| Argument | Default | Description |
|----------|---------|-------------|
| `project-path` | `.` | Project directory or model YAML file. |

**Options**

| Option | Description |
|--------|-------------|
| `--details` | Show column counts, hierarchy counts, measure counts, relationship counts, source paths, and lineage tags for each object. |

**Examples**

```bash
pbt list .
pbt list my_project --details
```

---

## import

Import existing definitions into pbt YAML format. Three subcommands cover different sources.

### pbt import model

Convert a TMDL model folder into a pbt project (table YAMLs + model YAML).

```
pbt import model <tmdl-path> [<output-path>] [options]
```

**Arguments**

| Argument | Default | Description |
|----------|---------|-------------|
| `tmdl-path` | _(required)_ | Path to the TMDL folder (must contain `.tmdl` files). |
| `output-path` | `.` | Directory where the YAML project will be written. `tables/` and `models/` sub-directories are created automatically. |

**Options**

| Option | Default | Description |
|--------|---------|-------------|
| `--include-lineage-tags` | false | Copy the original lineage tag GUIDs from TMDL into the generated YAML. Without this flag, new tags are generated on the next `pbt build`. Use this when migrating an existing model whose reports must stay connected. |
| `--overwrite` | false | Overwrite files if `output-path` already contains a project. Without this flag, the command aborts if the directory is not empty. |
| `--unsupported-objects <mode>` | `warn` | How to handle TMDL constructs not yet supported by pbt (perspectives, roles, calculation groups, translations). `warn` = print a warning and continue, `skip` = silently skip, `error` = abort. |
| `--show-changes` | false | Preview the diff of what would be written before applying. |
| `--auto-merge` | false | Apply changes without interactive confirmation. |

**Examples**

```bash
# Import and generate fresh lineage tags on next build
pbt import model /path/to/model.tmdl my_yaml_project

# Import and preserve original lineage tags
pbt import model /path/to/model.tmdl my_yaml_project --include-lineage-tags

# Overwrite an existing project
pbt import model /path/to/model.tmdl my_yaml_project --overwrite

# Abort if any unsupported constructs are found
pbt import model /path/to/model.tmdl my_yaml_project --unsupported-objects error
```

### pbt import table

Import individual table definitions from TMDL.

```
pbt import table <path> [<output-path>] [options]
```

**Arguments**

| Argument | Default | Description |
|----------|---------|-------------|
| `path` | _(required)_ | TMDL directory or `.tmdl` file. |
| `output-path` | `./tables` | Directory where table YAML files are written. |

**Options**

| Option | Description |
|--------|-------------|
| `--include-lineage-tags` | Preserve original lineage tags. |

**Smart merge behaviour**

When a table YAML file already exists at the output path, the import merges rather than overwrites:

- Column types are updated from the source (source of truth)
- New columns are added automatically
- Removed columns are kept by default (safer than silent deletion)
- Manual settings are preserved: `description`, `is_hidden`, `format_string`, `hierarchies`, `annotations`

```bash
pbt import table /path/to/model.tmdl
pbt import table /path/to/model.tmdl ./my_tables
pbt import table /path/to/model.tmdl --include-lineage-tags
```

**From a CSV schema export or Snowflake**: generate `.tmdl` with a standalone plugin, then import it.

```bash
csv-to-tmdl schema_export.csv --source-type sqlserver -o tmdl/        # plugins/csv_to_tmdl
snowflake-to-tmdl ANALYTICS_DB.PUBLIC -o tmdl/                        # plugins/snowflake_to_tmdl
pbt import table tmdl/ ./tables
```

---

## lineage

Manage the lineage tag manifest stored in `.pbt/lineage.yaml`.

Lineage tags are GUIDs that bind Power BI report visuals to specific model objects (tables, columns, measures). pbt generates them deterministically from object names so the same YAML always produces the same tags. The manifest is updated automatically on each `pbt build`.

### pbt lineage show

Display the current manifest.

```
pbt lineage show [<project-path>] [--details]
```

| Option | Description |
|--------|-------------|
| `--details` | Print every tag GUID for every table, column, measure, and relationship. |

```bash
pbt lineage show .
pbt lineage show my_project --details
```

### pbt lineage clean

Remove tags for objects that no longer exist in the project (orphaned tags from renamed or deleted tables/columns/measures).

```
pbt lineage clean [<project-path>] [--dry-run]
```

| Option | Description |
|--------|-------------|
| `--dry-run` | Show what would be removed without modifying the manifest. |

```bash
pbt lineage clean .
pbt lineage clean my_project --dry-run
```

### pbt lineage reset

Delete the entire manifest. The next `pbt build` will generate all-new lineage tags.

```
pbt lineage reset [<project-path>] --confirm
```

| Option | Description |
|--------|-------------|
| `--confirm` | Required. Acknowledges that all connected Power BI reports will break. |

> **Warning**: Resetting lineage tags means every visual in every connected report loses its binding to the model. Reports will show errors until re-bound. Only do this when you are intentionally severing all existing report connections.

```bash
pbt lineage reset my_project --confirm
```

**Version control note**: Commit `.pbt/lineage.yaml` to git so all team members share the same tags. If you add it to `.gitignore`, each developer's build generates independent tags and reports built on one developer's model will not open correctly on another's.

---

## diff

Compare two project states and classify each change as breaking or non-breaking.

```
pbt diff <path-a> <path-b> [options]
```

**Arguments**

| Argument | Description |
|----------|-------------|
| `path-a` | First project path (e.g., a previous snapshot or a git worktree checkout of an earlier commit). |
| `path-b` | Second project path (e.g., the current working copy). |

**Options**

| Option | Default | Description |
|--------|---------|-------------|
| `--breaking` | false | Exit with code `1` if any breaking changes are detected. Use in CI to gate deploys. |
| `--output <format>` | `text` | Output format: `text` or `json`. |

**Breaking changes** (exit 1 with `--breaking`):

- Table removed
- Column removed or its data type changed
- Measure removed
- Relationship removed
- Model removed

**Non-breaking changes** (noted but do not trigger `--breaking`):

- Table, column, measure, relationship, or model added
- Column description or format string changed
- Measure expression changed

**Examples**

```bash
# Compare two directories
pbt diff ./project_v1 ./project_v2

# JSON output for parsing in CI scripts
pbt diff ./old ./new --output json

# Fail CI pipeline if breaking changes are present
pbt diff ./baseline ./current --breaking
```

A common CI pattern is to check out the base branch into a temp directory, then diff against the current branch:

```bash
git worktree add /tmp/base-branch origin/main
pbt diff /tmp/base-branch . --breaking
git worktree remove /tmp/base-branch
```

---

## generate-tables

Expand table declarations in `pbt.yml` into `tables/<name>.yaml` by fetching columns from the source. `pbt build` then produces the TMDL.

```
pbt generate-tables [options]
```

**Options**

| Option | Default | Description |
|--------|---------|-------------|
| `--config <path>` | `pbt.yml` | Path to the config file. Tables are written to `tables/` next to it. |
| `--dry-run` | false | Report created / updated / unchanged / error per table without writing files (including model refs). |

Exit code `1` if any table fails; other tables are still processed. Supports global `--output-format json`.

**`pbt.yml`**

```yaml
sources:
  snowflake:
    connector: SnowflakeSource     # shared connector expression, or `connection:` literal (no secrets, no ${VAR})
model: models/sales_model.yaml     # optional: missing `ref:` entries are added here (YAML comments in it are lost)
tables:
  - table_name: Property
    source: snowflake
    target: db.schema.table        # unquoted parts fold to UPPER; "Quoted" keeps case
    options: { hidden: false, mode: import }
    column_overrides:              # keyed by source column
      PROPERTY_ID: { name: PropertyId, is_key: true }
      INTERNAL_CODE: { is_hidden: true }
      CREATED_AT: { data_type: DateTime, format_string: "yyyy-mm-dd" }
```

**Behaviour**

- Snowflake metadata comes from `plugins/snowflake_to_tmdl` (`python -m snowflake_to_tmdl`; set `PBT_PYTHON` to pick the interpreter). Credentials are read from that plugin's env vars (`SNOWFLAKE_ACCOUNT`, ...), never from or into project files.
- Re-runs merge into the existing table file: manual measures, hierarchies, descriptions and other table properties are kept; unchanged files are not rewritten. An unreadable existing file is an error, never overwritten. `column_overrides` always win.
- `options.mode` supports only `import`. `disable_auto_date_time: true` is accepted (it is the default); the real setting is model-level (`auto_time_intelligence`).

**Example**

```bash
pbt generate-tables --dry-run
pbt generate-tables --config ./pbt.yml
```

---

## validate-tables

Check `pbt.yml` table definitions offline (no source lookup): required fields, duplicate `table_name`, configured source, target component count, unsupported options.

```
pbt validate-tables [options]
```

**Options**

| Option | Default | Description |
|--------|---------|-------------|
| `--config <path>` | `pbt.yml` | Path to the config file. |

Exit code `1` if any definition is invalid. Supports global `--output-format json`.

---

## Global Behaviour

**Path resolution**: All commands that accept a `project-path` also accept a direct model YAML file path. When a file is given, pbt infers the project root from the file's parent directories and uses that for loading tables, environments, and the lineage manifest.

**Exit codes**: Commands exit `0` on success and `1` on any error. `pbt validate --strict` and `pbt diff --breaking` also exit `1` for warnings and breaking changes respectively.

**Pre-hook timeout**: `--pre-hook` commands time out after 60 seconds and are killed (including child processes) if they exceed that limit.
