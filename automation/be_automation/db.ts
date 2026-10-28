import pg from 'pg';

// Ages every session of a user so the API sees it as expired; there is no endpoint to do this.
export async function expireSessionsOf(email: string) {
  const client = new pg.Client({ connectionString: process.env.API_DB_CONNECTION });
  await client.connect();
  try {
    await client.query(
      `update user_sessions set expires_at = now() - interval '1 minute'
       where user_id = (select id from users where normalized_email = $1)`,
      [email.toLowerCase()],
    );
  } finally {
    await client.end();
  }
}
