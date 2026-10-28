import { deleteLeftovers } from './db';

// Start from a clean DB even if an earlier run crashed before its cleanup.
export default async function globalSetup() {
  await deleteLeftovers();
}
