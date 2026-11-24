const FILE = 'standup | daily | 09:30\nreview | weekly fri | 15:00\nretro | every 2 weeks wed from 2026-01-07 | 16:00\nrent | monthly 31 | 08:00\nboard | last thu of month | 10:00\nbad line here\n';
const count = (s, w) => (s.match(new RegExp(`\\b${w}\\b`, 'gi')) || []).length;
const near = (out, date, re) => { const i = out.indexOf(date); return i >= 0 && re.test(out.slice(Math.max(0, i - 40), i + 60)); };
export default async (c) => {
  c.write('ev.txt', FILE);
  const jan = c.run(['agenda', 'ev.txt', '--from', '2026-01-26', '--days', '14']);
  const feb = c.run(['agenda', 'ev.txt', '--from', '2026-02-01', '--days', '28']);
  const next = c.run(['next', 'ev.txt', 'rent', '--from', '2026-04-01']);
  const all = jan.out + jan.err;
  const iRent = jan.out.indexOf('rent'), iStand = jan.out.indexOf('standup', jan.out.indexOf('2026-01-31'));
  return [
    { name: 'Jan counts: standup 14, review 2, retro 1, rent 1, board 1', ok: count(jan.out, 'standup') === 14 && count(jan.out, 'review') === 2 && count(jan.out, 'retro') === 1 && count(jan.out, 'rent') === 1 && count(jan.out, 'board') === 1, got: ['standup', 'review', 'retro', 'rent', 'board'].map(w => count(jan.out, w)).join(',') + ' ' + jan.err.slice(0, 100) },
    { name: 'retro on 4 Feb, a Wednesday', ok: jan.out.includes('2026-02-04') && near(jan.out, '2026-02-04', /wed/i) && !/2026-01-28[^\n]*\n(?:(?!2026-)[^\n]*\n)*?[^\n]*retro/.test(jan.out), got: jan.out.slice(0, 200) },
    { name: 'rent before standup on 31 Jan', ok: iRent >= 0 && iStand > iRent, got: `${iRent} ${iStand}` },
    { name: 'Feb: no rent, board on 26 Feb', ok: count(feb.out, 'rent') === 0 && feb.out.includes('2026-02-26'), got: feb.out.slice(-200) },
    { name: 'next rent from 2026-04-01 is 2026-05-31', ok: next.out.includes('2026-05-31'), got: next.out + next.err },
    { name: 'line 6 reported', ok: /\b6\b/.test(all.replace(/2026-\d\d-\d\d|\d\d:\d\d/g, '')) && /pars|invalid|skip|bad|error/i.test(all), got: jan.err.slice(0, 150) },
  ];
};
