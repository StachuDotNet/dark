import { execSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
export default async (c) => {
  const h = c.write('h.txt', 'hello\n'); c.write('empty.txt', ''); c.write('bin.dat', Buffer.from(Array.from({ length: 300000 }, (_, i) => (i * 7919) % 256)));
  const want = execSync('sha256sum h.txt empty.txt bin.dat', { cwd: c.tmp }).toString().trim();
  const one = c.run(['h.txt'], { cwd: c.tmp });
  const many = c.run(['h.txt', 'empty.txt', 'bin.dat'], { cwd: c.tmp });
  c.write('sums', one.out);
  const ok = c.run(['--check', 'sums'], { cwd: c.tmp });
  c.write('h.txt', 'changed\n');
  const bad = c.run(['--check', 'sums'], { cwd: c.tmp });
  execSync('rm h.txt', { cwd: c.tmp });
  const gone = c.run(['--check', 'sums'], { cwd: c.tmp });
  return [
    { name: 'hello hash', ok: one.out.trim() === '5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03  h.txt', got: one.out + one.err },
    { name: 'matches sha256sum', ok: many.out.trim() === want, got: many.out },
    { name: 'check OK, exit 0', ok: /h\.txt: OK/.test(ok.out) && ok.code === 0, got: `${ok.code} ${ok.out}${ok.err}` },
    { name: 'changed: FAILED, exit 1', ok: /FAILED/.test(bad.out) && bad.code === 1, got: `${bad.code} ${bad.out}` },
    { name: 'missing: exit 1', ok: gone.code === 1, got: `${gone.code} ${gone.out}${gone.err}` },
  ];
};
