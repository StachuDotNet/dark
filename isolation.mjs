// What an agent CLI picks up from the folders above its workspace: instruction files and a .claude
// folder (its CLAUDE.md, skills, commands, agents, settings). A bench workspace must have none above it.
import { existsSync } from 'node:fs';
import path from 'node:path';

const SETUP = ['CLAUDE.md', 'CLAUDE.local.md', 'AGENTS.md', '.claude'];

export function setupAbove(dir) {
  const found = [];
  for (let d = path.dirname(path.resolve(dir)); ; d = path.dirname(d)) {
    for (const f of SETUP) if (existsSync(path.join(d, f))) found.push(path.join(d, f));
    if (path.dirname(d) === d) break;
  }
  return found;
}
