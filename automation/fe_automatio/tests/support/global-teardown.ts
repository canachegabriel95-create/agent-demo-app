import { countLeftovers } from './db';

// The suite must leave the DB as it found it.
export default async function globalTeardown() {
  const leftovers = await countLeftovers();
  if (leftovers > 0) {
    throw new Error(`${leftovers} e2e user(s) were left in the database.`);
  }
}
