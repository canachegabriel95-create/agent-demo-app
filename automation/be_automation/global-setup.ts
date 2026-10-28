import pg from 'pg';

// Test data is recognisable by its email domain (see fixtures.ts). The admin's own sessions, invitations and
// audit events from the run are removed by timestamp.
export const TEST_EMAIL_PATTERN = '%@example.test';

export default async function globalSetup() {
  const connectionString = process.env.API_DB_CONNECTION;
  if (!connectionString) throw new Error('Set API_DB_CONNECTION (see .env.example) so test data can be cleaned up.');

  const started = new Date();
  return async function cleanDatabase() {
    const client = new pg.Client({ connectionString });
    await client.connect();
    try {
      await client.query('begin');
      const testUsers = `select id from users where normalized_email like $1`;
      const testArticles = `select id from articles where author_id in (${testUsers})`;
      const p = [TEST_EMAIL_PATTERN, started];
      await client.query(`delete from reviews where article_id in (${testArticles}) or reviewer_id in (${testUsers})`, [p[0]]);
      await client.query(`delete from article_submissions where article_id in (${testArticles})`, [p[0]]);
      await client.query(`delete from articles where author_id in (${testUsers})`, [p[0]]);
      await client.query(`delete from audit_events where occurred_at >= $2 or actor_user_id in (${testUsers})`, p);
      await client.query(`delete from invitations where normalized_email like $1 or created_at >= $2`, p);
      await client.query(`delete from user_sessions where created_at >= $2 or user_id in (${testUsers})`, p);
      await client.query(`delete from users where normalized_email like $1`, [p[0]]);
      await client.query('commit');
    } catch (error) {
      await client.query('rollback');
      throw error;
    } finally {
      await client.end();
    }
  };
}
