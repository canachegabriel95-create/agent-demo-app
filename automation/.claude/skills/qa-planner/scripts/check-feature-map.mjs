// Checks that every doc and api.md section named in references/feature-map.md still exists.
// Usage: node .claude/skills/qa-planner/scripts/check-feature-map.mjs [repoRoot]
// Exit 0 = map is in sync, 1 = stale entries (listed), 2 = could not read the inputs.
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const skillDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = resolve(process.argv[2] ?? join(skillDir, '../../../..'));
const docsDir = join(repoRoot, 'docs');

let map, api;
try {
  map = readFileSync(join(skillDir, 'references/feature-map.md'), 'utf8');
  api = readFileSync(join(docsDir, 'api.md'), 'utf8');
} catch (e) {
  console.error(`Cannot read inputs: ${e.message}`);
  process.exit(2);
}

const h2 = new Set(api.split(/\r?\n/).filter(l => l.startsWith('## ')).map(l => l.slice(3).trim()));
const h3 = new Set(api.split(/\r?\n/).filter(l => l.startsWith('### ')).map(l => l.slice(4).trim().replace(/`/g, '')));

const problems = [];
const rows = map.split(/\r?\n/).filter(l => /^\| F\d{2} /.test(l));
if (rows.length === 0) problems.push('no feature rows found in feature-map.md');

for (const row of rows) {
  const cells = row.split('|').slice(1, -1).map(c => c.trim());
  const [feature, , pageDocs, apiSections, , crossDocs] = cells;
  const code = feature.split(' ')[0];

  // Docs (page + cross-cutting). Docs marked "not written yet" may be missing.
  for (const cell of [pageDocs, crossDocs]) {
    const pending = /not written yet/.test(cell);
    for (const [, doc] of cell.matchAll(/`([\w-]+\.md)`/g)) {
      if (!existsSync(join(docsDir, doc))) {
        if (!pending) problems.push(`${code}: docs/${doc} does not exist`);
      } else if (pending) {
        problems.push(`${code}: docs/${doc} now exists — remove "not written yet"`);
      }
    }
  }

  // api.md sections: "Heading; Heading → "Sub", "Sub""
  if (!apiSections.startsWith('—')) {
    for (const part of apiSections.split(';')) {
      const [heading, subs] = part.split('→').map(s => s.trim());
      if (!h2.has(heading)) problems.push(`${code}: api.md has no "## ${heading}"`);
      for (const [, sub] of (subs ?? '').matchAll(/"([^"]+)"/g)) {
        if (!h3.has(sub)) problems.push(`${code}: api.md has no "### ${sub}"`);
      }
    }
  }
}

// A doc in docs/ that the map never mentions is probably a new page the map is missing.
const mentioned = new Set([...map.matchAll(/`([\w-]+\.md)`/g)].map(m => m[1]));
const known = ['README.md', 'api.md'];
for (const f of readdirSync(docsDir).filter(f => f.endsWith('.md'))) {
  if (!mentioned.has(f) && !known.includes(f)) problems.push(`docs/${f} is not in the feature map`);
}

if (problems.length) {
  console.log(`feature-map.md is stale (${problems.length}):`);
  for (const p of problems) console.log(`  - ${p}`);
  process.exit(1);
}
console.log(`feature-map.md OK: ${rows.length} features checked against ${docsDir}`);
