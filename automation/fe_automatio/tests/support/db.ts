import { pbkdf2Sync, randomBytes, randomUUID } from 'node:crypto';
import { Client } from 'pg';

/** Every e2e user gets this email prefix, so leftovers are easy to find and remove. */
export const E2E_EMAIL_PREFIX = 'e2e-';

export interface TestUser {
  id: string;
  email: string;
  password: string;
}

export interface NewUser {
  role: 'Administrator' | 'Author';
  status?: 'Active' | 'Suspended';
}

function databaseUrl(): string {
  const url = process.env.E2E_DATABASE_URL;
  if (!url) {
    throw new Error('E2E_DATABASE_URL is not set (e.g. postgres://user:password@localhost:5432/content_review).');
  }
  return url;
}

async function withDb<T>(work: (db: Client) => Promise<T>): Promise<T> {
  const db = new Client({ connectionString: databaseUrl() });
  await db.connect();
  try {
    return await work(db);
  } finally {
    await db.end();
  }
}

/** ASP.NET Core Identity V3 hash (PBKDF2-HMAC-SHA512), the format PasswordHasher verifies. */
function hashPassword(password: string): string {
  const iterations = 100_000;
  const salt = randomBytes(16);
  const key = pbkdf2Sync(password, salt, iterations, 32, 'sha512');
  const header = Buffer.alloc(13);
  header.writeUInt8(1, 0);
  header.writeUInt32BE(2, 1); // prf: HMACSHA512
  header.writeUInt32BE(iterations, 5);
  header.writeUInt32BE(salt.length, 9);
  return Buffer.concat([header, salt, key]).toString('base64');
}

export async function createUser({ role, status = 'Active' }: NewUser): Promise<TestUser> {
  const id = randomUUID();
  const email = `${E2E_EMAIL_PREFIX}${id}@example.test`;
  // Random per user; satisfies the password policy (upper, lower, digit, symbol, 12+ chars).
  const password = `Aa1!${randomBytes(12).toString('hex')}`;
  const suspended = status === 'Suspended';

  await withDb((db) =>
    db.query(
      `INSERT INTO users (id, email, normalized_email, password_hash, full_name, role, status,
                          suspension_reason, suspended_at, created_at, updated_at)
       VALUES ($1, $2, $2, $3, $4, $5, $6, $7, $8, now(), now())`,
      [
        id,
        email,
        hashPassword(password),
        'E2E Test User',
        role,
        status,
        suspended ? 'Suspended by an automated test.' : null,
        suspended ? new Date() : null,
      ],
    ),
  );

  return { id, email, password };
}

export async function suspendUser(id: string): Promise<void> {
  await withDb((db) =>
    db.query(
      `UPDATE users SET status = 'Suspended', suspension_reason = 'Suspended by an automated test.',
                        suspended_at = now(), updated_at = now() WHERE id = $1`,
      [id],
    ),
  );
}

/** Removes the given users and everything that points at them. */
export async function deleteUsers(ids: string[]): Promise<void> {
  if (ids.length === 0) return;
  await withDb(async (db) => {
    await db.query('BEGIN');
    await db.query('DELETE FROM audit_events WHERE actor_user_id = ANY($1) OR entity_id = ANY($1)', [ids]);
    await db.query('DELETE FROM users WHERE id = ANY($1)', [ids]); // sessions cascade
    await db.query('COMMIT');
  });
}

/** Removes every e2e user, e.g. leftovers of a crashed run. */
export async function deleteLeftovers(): Promise<void> {
  const ids = await withDb(async (db) => {
    const { rows } = await db.query<{ id: string }>('SELECT id FROM users WHERE normalized_email LIKE $1', [
      `${E2E_EMAIL_PREFIX}%`,
    ]);
    return rows.map((row) => row.id);
  });
  await deleteUsers(ids);
}

/** Counts leftover e2e rows; the suite is clean when this is 0. */
export async function countLeftovers(): Promise<number> {
  return withDb(async (db) => {
    const { rows } = await db.query<{ n: string }>(
      'SELECT count(*) AS n FROM users WHERE normalized_email LIKE $1',
      [`${E2E_EMAIL_PREFIX}%`],
    );
    return Number(rows[0].n);
  });
}
