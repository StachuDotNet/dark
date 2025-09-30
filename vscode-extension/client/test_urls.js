const testUrls = [
  'dark:///session/feature-auth',
  'dark:///patch/abc123/operations',
  'dark:///patch/abc123/conflicts', 
  'dark:///patch/abc123/tests',
  'dark:///patch/abc123/test?name=validation_test',
  'dark:///instance/matter-prod/packages',
  'dark:///instance/matter-prod/sessions'
];

console.log('Clean URLs demonstrate the new system:');
testUrls.forEach(url => {
  const pathParts = url.split('/').filter(p => p && !p.includes('?'));
  const lastPart = pathParts[pathParts.length - 1] || 'unknown';
  
  console.log(`  ${url}`);
  console.log(`    → VS Code tab shows: "${lastPart}"`);
  console.log(`    → Central system provides: title, badge, content provider`);
  console.log('');
});
