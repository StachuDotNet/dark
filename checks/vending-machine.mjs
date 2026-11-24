// The stock file's shape is the agent's choice, so only the tests are graded.
export default async (c) => {
  const t = c.test(c.owner().split('.')[1]);
  return [{ name: 'tests run and pass', ok: t.passed > 0 && t.failed === 0, got: JSON.stringify(t).slice(0, 150) }];
};
