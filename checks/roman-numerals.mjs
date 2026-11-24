import { pick } from '../grade-lib.mjs';
export const opt = (f) => /^Result</.test(f.ret) ? (x) => `(match ${f.name}(${x}) with | Ok(v) -> Stdlib.Option.Option.Some(v) | Error(_) -> Stdlib.Option.Option.None)` : /^Option</.test(f.ret) ? (x) => `${f.name}(${x})` : (x) => `Stdlib.Option.Option.Some(${f.name}(${x}))`;
export default async (c) => {
  const fns = c.fns().filter(f => !/Tests?$/.test(f.module));
  const to = pick(fns, /^(to|int|encode|format)/i, f => /^\w+\s*:\s*Int64$/.test(f.params) && /String/.test(f.ret));
  const from = pick(fns, /^(from|parse|decode)/i, f => /^\w+\s*:\s*String$/.test(f.params) && /Int64/.test(f.ret));
  if (!to || !from) return [{ name: 'found both functions by name', ok: false, got: fns.map(f => `${f.name}(${f.params}): ${f.ret}`).join('; ') }];
  const T = opt(to), F = opt(from);
  const ev = (e) => c.eval(e).out.trim();
  const S = (x) => `Stdlib.Option.Option.Some(${x})`, N = 'Stdlib.Option.Option.None';
  const t = c.test(c.owner().split('.')[1]);
  return [
    { name: 'found both functions by name', ok: true },
    { name: '1994, 3999, 4', ok: ev(`[${T(1994)} == ${S('"MCMXCIV"')}, ${T(3999)} == ${S('"MMMCMXCIX"')}, ${T(4)} == ${S('"IV"')}]`) === '[true, true, true]', got: ev(`[${T(1994)}, ${T(4)}]`) },
    { name: 'round trip 1..3999', ok: ev(`Stdlib.List.all(Stdlib.List.range(1, 3999), fun n -> match ${T('n')} with | Some(r) -> ${F('r')} == Stdlib.Option.Option.Some(n) | None -> false)`) === 'true', got: ev(F('"MCMXCIV"')) },
    { name: '0, 4000, -1 refused', ok: ev(`[${T(0)} == ${N}, ${T(4000)} == ${N}, ${T(-1)} == ${N}]`) === '[true, true, true]' },
    { name: 'non-canonical refused', ok: ev(`Stdlib.List.all(["IIII", "VX", "MMMM", "IC", "", "abc"], fun s -> ${F('s')} == ${N})`) === 'true' },
    { name: 'tests run and pass', ok: t.passed > 0 && t.failed === 0, got: JSON.stringify(t).slice(0, 150) },
  ];
};
