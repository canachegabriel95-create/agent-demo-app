import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { Pool, type QueryResultRow } from 'pg';

// Connection settings come from .env (loaded in playwright.config.ts).
let pool: Pool | undefined;

function getPool(): Pool {
  pool ??= new Pool({
    host: process.env.POSTGRES_HOST ?? 'localhost',
    port: Number(process.env.POSTGRES_PORT ?? 5432),
    database: process.env.POSTGRES_DB ?? 'content_review',
    user: process.env.POSTGRES_USER,
    password: process.env.POSTGRES_PASSWORD,
  });
  return pool;
}

/** Runs db/queries/<name>.sql with positional parameters ($1, $2, ...) and returns the rows. */
export async function runQuery<T extends QueryResultRow>(name: string, params: unknown[] = []): Promise<T[]> {
  const sql = readFileSync(join(__dirname, 'queries', `${name}.sql`), 'utf8');
  const result = await getPool().query<T>(sql, params);
  return result.rows;
}

export async function closeDb(): Promise<void> {
  await pool?.end();
  pool = undefined;
}
