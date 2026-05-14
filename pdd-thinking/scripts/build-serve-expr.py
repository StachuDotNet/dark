#!/usr/bin/env python3
"""Generate the Dark router expression for the darklang.com PDD port.

Walks ~/code/darklang.com/src/pages, extracts text content from each
page's TSX file, and builds the `Builtin.httpServerServe` expression
with one route per page. Writes to /tmp/serve-expr.txt for the CLI to
exec via `dark pdd run "$(cat /tmp/serve-expr.txt)"`.

To run: `python3 pdd-thinking/scripts/build-serve-expr.py`
"""
import re, os

PAGES_DIR = '/home/stachu/code/darklang.com/src/pages'

# (URL, file under PAGES_DIR, materializer name)
# Order matters: more specific routes FIRST (because endsWith semantics
# would otherwise match `/for/web-developers` against the `/for` route).
routes = [
  ('/for/web-developers',   'For/WebDevelopers.tsx',     'renderForWebDev'),
  ('/for/python-developers','For/PythonDevelopers.tsx',  'renderForPython'),
  ('/for/ai-developers',    'For/AIDevelopers.tsx',      'renderForAI'),
  ('/for/security-nerds',   'For/SecurityNerds.tsx',     'renderForSecurity'),
  ('/for/fsharp-developers','For/FSharpDevelopers.tsx',  'renderForFSharp'),
  ('/for/small-businesses', 'For/SmallBusinesses.tsx',   'renderForSmall'),
  ('/for/local-first',      'For/LocalFirst.tsx',        'renderForLocal'),
  ('/for/web-scrapers',     'For/WebScrapers.tsx',       'renderForScrape'),
  ('/for/lazy-people',      'For/LazyPeople.tsx',        'renderForLazy'),
  ('/company/sustainability','Company/Sustainability.tsx', 'renderSustain'),
  ('/for',           'For/index.tsx',           'renderForX'),
  ('/classic',       'Classic/index.tsx',       'renderClassic'),
  ('/language',      'Language/index.tsx',      'renderLanguage'),
  ('/editing',       'Editing/index.tsx',       'renderEditing'),
  ('/type-checking', 'TypeChecking/index.tsx',  'renderTypes'),
  ('/execution',     'Execution/index.tsx',     'renderExecution'),
  ('/distribution',  'Distribution/index.tsx',  'renderDistribution'),
  ('/traceDriven',   'TraceDriven/index.tsx',   'renderTraceDriven'),
  ('/source-control','SourceControl/index.tsx', 'renderSourceCtl'),
  ('/cli',           'CLI/index.tsx',           'renderCli'),
  ('/backends',      'Backends/index.tsx',      'renderBackends'),
  ('/ai',            'AI/index.tsx',            'renderAI'),
  ('/getting-started','GettingStarted/index.tsx','renderGettingStarted'),
  ('/our-cloud',     'Cloud/index.tsx',         'renderCloud'),
  ('/company',       'Company/index.tsx',       'renderCompany'),
  ('/newsletter',    'Newsletter/index.tsx',    'renderNewsletter'),
  ('/support',       'Support/index.tsx',       'renderSupport'),
  ('/sharing',       'Sharing/index.tsx',       'renderSharing'),
  ('/no',            'No/index.tsx',            'renderNo'),
  ('/stats',         'Stats/index.tsx',         'renderStats'),
  ('/packages',      'Packages/index.tsx',      'renderPackages'),
  ('/roadmap',       'Roadmap.tsx',             'renderRoadmap'),
]
HOME_PATH = 'Home/index.tsx'

def extract(rel):
    p = os.path.join(PAGES_DIR, rel)
    try:
        with open(p) as f: c = f.read()
    except Exception:
        return ''
    items = re.findall(r'>\s*([^<>{}]+?)\s*</', c)
    items = [i.strip() for i in items if 3 < len(i.strip()) < 200]
    s = ' | '.join(items[:20])[:700]
    return s.replace('"', '\\"').replace('\n', ' ')

home_ctx = (extract(HOME_PATH) or "darklang.com home").replace('"', '\\"').replace('\n', ' ')

chain = ""
for url, path, fn in routes:
    ctx = extract(path) or f"darklang.com page {url}"
    chain += f'if Stdlib.String.endsWith req.url "{url}" then Stdlib.Http.responseWithHtml ({fn} "{ctx}") 200L else '
chain += f'Stdlib.Http.responseWithHtml (renderHome "{home_ctx}") 200L'

expr = f'Builtin.httpServerServe 9876L (fun req -> {chain}) 1000000L true true true'
with open('/tmp/serve-expr.txt', 'w') as f: f.write(expr)
print(f"wrote /tmp/serve-expr.txt — {len(expr)} chars, {len(routes)} routes")
