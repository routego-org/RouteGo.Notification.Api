# migrations/

The database schema of `routego_notification`, as raw versioned SQL. The application never
applies these scripts. Until a CI/CD pipeline exists, `local/apply-migrations.sh` applies them —
locally with `docker compose run --rm migrate`, and on EC2 before the container starts (deploy
runbook step 5). A future pipeline DbUp step replaces the script and must keep the same contract.

## What any runner of this folder must do

| Property | Why |
| --- | --- |
| Variable substitution **disabled** | The scripts use PL/pgSQL `$$` bodies, which DbUp's `$variable$` preprocessor mangles |
| **No wrapping transaction** | Every script owns its `BEGIN`/`COMMIT` |
| `R__*.sql` as **RunAlways**, **after** every `V####` | `R__audit_partition_runway.sql` tops up the audit partition runway on every run |
| Journal at **`jobs.schema_versions`** | `public` holds no tables |
| The **`jobs` schema** is ensured before the journal table | The runner creates the journal table, not its schema |
| A missing or empty `migrations/` **fails** the run | A run that applies nothing must not look like success |

## Naming

`V####__snake_case_description.sql`, created with
`.agents/skills/skills-db-postgres/scripts/new-migration.ps1`, and linted with
`python .agents/skills/skills-db-postgres/scripts/check_migrations.py migrations`.

## Running it

```sh
docker compose up -d postgres
docker compose run --rm migrate
```

Database users and their permissions are created by the database administrator, out of band —
never by these scripts.
