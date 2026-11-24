const shuffle = (xs, seed) => { const a = [...xs]; let s = seed; for (let i = a.length - 1; i > 0; i--) { s = (s * 1103515245 + 12345) % 2147483648; const j = s % (i + 1); [a[i], a[j]] = [a[j], a[i]]; } return a; };
const lost = (out) => /\byou (win|won)\b|player wins|human wins|congratulations|you beat/i.test(out.split('\n').slice(-6).join('\n'));
export default async (c) => {
  const games = [];
  for (let g = 0; g < 20; g++) {
    const moves = shuffle([1, 2, 3, 4, 5, 6, 7, 8, 9], g + 1).join('\n') + '\n';
    games.push(c.run(g % 2 ? ['--first', 'computer'] : [], { stdin: moves, timeout: 60000 }));
  }
  const bad = c.run([], { stdin: '5\n5\n0\n10\nx\n1\n2\n3\n4\n6\n7\n8\n9\n' });
  const seq = c.run([], { stdin: '1\n2\n3\n4\n5\n6\n7\n8\n9\n' });
  const ended = (r) => r.code === 0 && /draw|tie|win|won|lose|lost/i.test(r.out.split('\n').slice(-6).join(' '));
  return [
    { name: 'piped game ends, exit 0', ok: ended(seq), got: `${seq.code} ${seq.out.slice(-200)}${seq.err}` },
    { name: 'bad input rejected, game goes on', ok: ended(bad) && /invalid|occupied|taken|not|try/i.test(bad.out + bad.err), got: bad.out.slice(-200) },
    { name: '20 random games, never loses', ok: games.every(ended) && !games.some(r => lost(r.out)), got: games.map(r => `${r.code}${lost(r.out) ? 'L' : ''}`).join(' ') },
  ];
};
