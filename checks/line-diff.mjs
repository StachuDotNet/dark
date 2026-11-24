import { execSync } from 'node:child_process';
const body = (s) => s.split('\n').filter(l => !/^(---|\+\+\+) /.test(l)).join('\n').trim();
export default async (c) => {
  const sh = (cmd) => execSync(cmd, { cwd: c.tmp, shell: '/bin/bash' });
  const gnu = (a, b, u = []) => { try { return execSync(`diff ${u.length ? u.join(' ') : '-u'} ${a} ${b}`, { cwd: c.tmp }).toString(); } catch (e) { return String(e.stdout); } };
  sh('seq 1 20 > a; cp a same; seq 1 20 | sed "s/^5$/five/; /^15$/d" > b; seq 1 100 > m1; seq 1 100 | sed "s/^50$/x/" > m2; : > e; printf "1\\n2\\n3\\n" > t3');
  sh('for i in $(seq 1 2000); do echo "line $i"; done > big1; awk \'{ if (NR % 40 == 0) print "changed " NR; else print }\' big1 > big2');
  const same = c.run(['a', 'same']), ab = c.run(['a', 'b']), mid = c.run(['m1', 'm2']), u0 = c.run(['-U', '0', 'm1', 'm2']), emp = c.run(['e', 't3']);
  const t0 = Date.now(), big = c.run(['big1', 'big2'], { timeout: 30000 }), bigS = (Date.now() - t0) / 1000;
  const miss = c.run(['a', 'nope']);
  return [
    { name: 'identical: no output, exit 0', ok: same.code === 0 && same.out.trim() === '', got: `${same.code} ${same.out}${same.err}` },
    { name: 'seq example matches diff -u, exit 1', ok: ab.code === 1 && body(ab.out) === body(gnu('a', 'b')) && /^--- a/m.test(ab.out) && /^\+\+\+ b/m.test(ab.out), got: ab.out.slice(0, 200) },
    { name: 'one hunk, 3 context', ok: body(mid.out) === body(gnu('m1', 'm2')), got: mid.out.slice(0, 200) },
    { name: '-U 0', ok: body(u0.out) === body(gnu('m1', 'm2', ['-U', '0'])), got: u0.out.slice(0, 200) },
    { name: 'empty vs 3 lines', ok: /@@ -0,0 \+1,3 @@/.test(emp.out), got: emp.out.slice(0, 120) },
    { name: '2,000 lines, 50 changes, under 10 s', ok: bigS < 10 && body(big.out) === body(gnu('big1', 'big2')), got: `${bigS}s` },
    { name: 'missing file exits 2', ok: miss.code === 2, got: `${miss.code} ${miss.err}` },
  ];
};
