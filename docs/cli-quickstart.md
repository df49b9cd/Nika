# Nika CLI Quickstart

The Nika CLI wraps the migration runner so teams can execute versioned scripts from the command line with predictable, explicit behaviour. This guide walks through a minimal setup that uses the bundled file system migration source and JSON-backed version store.

## 1. Create a Project Layout

```
your-app/
├── migrations/
│   ├── 0001_create_table.up.sql
│   ├── 0001_create_table.down.sql
│   └── 0002_seed_data.up.sql
├── nika.config.json
└── state/ (created automatically)
```

Migration filenames follow the pattern `&lt;version&gt;_description.up.sql` / `down`. The numeric prefix controls ordering and must be unique.

Example script (`migrations/0001_create_table.up.sql`):

```sql
CREATE TABLE items (
    id INT PRIMARY KEY,
    name TEXT NOT NULL
);
```

## 2. Configure the CLI

Add `nika.config.json` next to your migrations:

```json
{
  "stateFile": "state/state.json",
  "migrationsPath": "migrations"
}
```

- `stateFile` is a JSON document that stores the last applied version and dirty flag. The CLI creates parent directories as needed.
- `migrationsPath` can be relative to the config file or an absolute path. If omitted, the CLI defaults to a `migrations` directory alongside the config file.
- To execute scripts against a PostgreSQL database, add a `driver` section:

```json
{
  "stateFile": "state/state.json",
  "migrationsPath": "migrations",
  "templates": {
    "up": "templates/up.sql.tpl",
    "down": "templates/down.sql.tpl"
  },
  "driver": {
    "name": "postgres",
    "connectionString": "Host=${PGHOST:-localhost};Username=${PGUSER};Password=${PGPASSWORD};Database=${PGDATABASE}",
    "commandTimeoutSeconds": 60,
    "searchPath": "public"
  }
}
```

The connection string supports `${ENV_VAR}` interpolation with optional defaults. Alternatively, specify `"connectionStringEnv": "DATABASE_URL"` to resolve the entire value from an environment variable.
Templates are optional text files whose contents will be copied into newly scaffolded migrations when you run `nika create`. You can also provide driver-specific templates by adding a `driverTemplates` block (e.g. `{ "postgres": { "up": "templates/pg-up.sql", "down": "templates/pg-down.sql" } }`), and CLI flags such as `--template-up` / `--template-down` always take precedence for ad-hoc overrides.

- For SQL Server environments:

```json
{
  "stateFile": "state/state.json",
  "migrationsPath": "migrations",
  "driver": {
    "name": "sqlserver",
    "connectionString": "Server=${MSSQL_HOST:-localhost},1433;User Id=sa;Password=${MSSQL_PASSWORD};TrustServerCertificate=True;",
    "commandTimeoutSeconds": 60,
    "useTransactions": true
  }
}
```

When targeting containers, be sure to supply `MSSQL_PASSWORD` and expose port 1433. The SQL Server driver automatically splits batches on `GO` statements.

## 3. Run Migrations

```bash
# Apply all pending migrations
dotnet run --project src/Nika.Cli -- --config nika.config.json up

# Apply a limited number of steps
dotnet run --project src/Nika.Cli -- --config nika.config.json up --steps 1

# Roll back the most recent migration
dotnet run --project src/Nika.Cli -- --config nika.config.json down

# Jump directly to a specific version (up or down as required)
dotnet run --project src/Nika.Cli -- --config nika.config.json goto 5

# Force the recorded version without executing scripts
dotnet run --project src/Nika.Cli -- --config nika.config.json force 0

# Drop all applied migrations (use --force to clear dirty state)
dotnet run --project src/Nika.Cli -- --config nika.config.json drop --force

# Inspect current version state
dotnet run --project src/Nika.Cli -- --config nika.config.json version

# Scaffold a new migration (creates .up.sql and .down.sql)
dotnet run --project src/Nika.Cli -- --config nika.config.json create add_users

# Override templates on the fly
dotnet run --project src/Nika.Cli -- --config nika.config.json create add_widget --template-up drafts/up.sql --template-down drafts/down.sql
```

Each command prints the executed steps and the resulting version. When a script fails, the CLI marks the state file as dirty so subsequent runs can diagnose and recover before continuing.

## 4. Inline Migrations (Optional)

During early prototyping you can embed migrations directly inside the config file:

```json
{
  "stateFile": "state/state.json",
  "migrations": [
    {
      "version": 1,
      "description": "Initial schema",
      "upMessage": "Create schema",
      "downMessage": "Drop schema"
    }
  ]
}
```

Inline migrations are executed by the CLI driver only and are useful for integration tests or demonstrations. For real database workflows, switch to file-based scripts and implement an `IScriptMigrationDriver` that executes SQL against your target server.

## 5. Next Steps

- Implement a database-specific driver that implements `IScriptMigrationDriver.ExecuteScriptAsync` to run SQL content against your database.
- Extend the configuration schema with connection strings or environment-variable expansion.
- Add your workflow to CI so migrations run automatically before deployment.

Refer to [`docs/driver-and-source-guide.md`](driver-and-source-guide.md) for implementation details and extension guidelines.
