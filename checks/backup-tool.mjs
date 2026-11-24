import { execSync } from 'node:child_process';
export default async (c) => {
  const sh = (cmd) => execSync(cmd, { cwd: c.tmp, shell: '/bin/bash' }).toString();
  sh('mkdir src; printf "aaa\\n" > src/a.txt; printf "bbb\\n" > src/b.txt; printf "c" > src/c.md');
  const al = ['env read BACKUP_MAX_BYTES'];
  const r1 = c.run(['src', 'dst'], { allow: al });
  const files1 = sh('ls dst 2>/dev/null | grep -v backup.log || true').trim().split('\n').filter(Boolean).sort().join(',');
  const same = sh('cmp src/a.txt dst/a.txt && cmp src/b.txt dst/b.txt && echo same || true').trim();
  const r2 = c.run(['src', 'dst'], { allow: al });
  sh('printf "changed\\n" > src/a.txt');
  const r3 = c.run(['src', 'dst'], { allow: al });
  const log = sh('cat dst/backup.log 2>/dev/null || true').trim().split('\n').filter(Boolean);
  sh('printf "0123456789" > src/big.txt');
  const r4 = c.run(['src', 'dst'], { allow: al, env: { BACKUP_MAX_BYTES: '3' } });
  const big = sh('[ -e dst/big.txt ] && echo copied || echo skipped').trim();
  const miss = c.run(['missing', 'dst'], { allow: al });
  const nums = (l) => (l.replace(/\d{8}T\d{4,6}(\.\d+)?Z?|\d{4}-\d\d-\d\d|\d\d:\d\d(:\d\d)?(\.\d+)?|T|Z/g, ' ').match(/\d+/g) || []).map(Number);
  return [
    { name: 'copies the .txt files only', ok: r1.code === 0 && files1 === 'a.txt,b.txt' && same === 'same', got: `${r1.code} ${files1} ${r1.err.slice(0, 120)}` },
    { name: 'log: 3 lines, counts 2/0, 0/2, 1/1', ok: log.length === 3 && [[2, 0], [0, 2], [1, 1]].every(([cp, sk], i) => nums(log[i] || '').includes(cp) && nums(log[i] || '').includes(sk)) && /\d\d:\d\d|\d{8}T\d{4}/.test(log[0] || ''), got: log.join(' | ') },
    { name: 'max bytes skips and names big.txt', ok: big === 'skipped' && /big\.txt/.test(r4.out + r4.err), got: `${big} ${r4.out}${r4.err}`.slice(0, 150) },
    { name: 'missing source exits 1 with stderr', ok: miss.code === 1 && miss.err.trim() !== '', got: `${miss.code} ${miss.err}` },
  ];
};
