// Shared by the pages: how to call into the runtime, how to boot it and load the store,
// and the two helpers every page needs to read a command's output.
//
// `dark.boot(onStatus)` resolves once the CLI can run (`Cli.Boot` done). `dark.run(argv)`
// runs one command with its output captured and returns { output, code }; the argv
// splitter honours double quotes. `dark.takesTheScreen(argv)` is the list of commands that
// need a real terminal, which the one-shot pages refuse.
//
// The tab keeps its work across reloads: see "Keeping a tab's work" below.
window.dark = (() => {
  const invoke = (name, ...args) => DotNet.invokeMethodAsync("Darklang.Wasm", name, ...args);
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  const screenCommands = new Set(["workbench", "wb", "outliner", "tree-exp", "views", "apps", "text-editor", "agent", "ai"]);

  // ---- Keeping a tab's work ----
  //
  // The store lives in emscripten's in-memory filesystem, under /dark, and is gone on reload. So the
  // page keeps a copy in the browser's IndexedDB, puts it back before Boot, and Boot decides what to
  // do with it (`Cli.Boot`, `RestoreOutcome`): use it, bring it up to this build first, or refuse it
  // and start fresh. No Dark code is involved and nothing new is granted; it is the host keeping its
  // own store, as the desktop CLI keeps ~/.darklang/data.db.
  //
  // Not kept: credentials.db (any script on this origin can read IndexedDB, and the terminal page
  // loads xterm from a CDN), SQLite's -shm (rebuilt on open), logs, and backups.
  const SAVED = "store";
  const kept = (name) => !(name === "logs" || name === "backups" || name === "credentials.db"
    || name === "incoming-store.db" || name.endsWith("-shm"));
  const fs = () => Blazor.runtime.Module.FS;
  let db = null;
  const openDb = () => db || (db = new Promise((res, rej) => {
    const r = indexedDB.open("darklang-tab", 1);
    r.onupgradeneeded = () => r.result.createObjectStore("files");
    r.onsuccess = () => res(r.result); r.onerror = () => rej(r.error);
  }));
  const request = async (mode, f) => {
    const d = await openDb();
    return new Promise((res, rej) => {
      const tx = d.transaction("files", mode); const r = f(tx.objectStore("files"));
      tx.oncomplete = () => res(r && r.result); tx.onerror = () => rej(tx.error); tx.onabort = () => rej(tx.error);
    });
  };
  const idbGet = (key) => request("readonly", (st) => st.get(key));
  const idbPut = (key, value) => request("readwrite", (st) => st.put(value, key));
  const idbDelete = (key) => request("readwrite", (st) => st.delete(key));

  // Every kept file under /dark: path -> bytes, or, with `statsOnly`, path -> "size:mtime".
  function walk(dir, out, statsOnly) {
    for (const name of fs().readdir(dir)) {
      if (name === "." || name === ".." || !kept(name)) continue;
      const path = dir + "/" + name; const st = fs().stat(path);
      if (fs().isDir(st.mode)) walk(path, out, statsOnly);
      else out[path] = statsOnly ? st.size + ":" + st.mtime.getTime() : fs().readFile(path);
    }
    return out;
  }
  const signature = () => JSON.stringify(walk("/dark", {}, true));

  let persisting = false, lastSaved = null, restoreOutcome = "";

  // Save the store if any kept file changed since the last save. A whole snapshot each time: the store
  // is one file and SQLite rewrites pages all over it, so there is nothing smaller to keep. Saves run
  // one after another, so a caller that awaits one knows the work it did before is kept.
  let queue = Promise.resolve();
  function save() {
    queue = queue.then(async () => {
      if (!persisting) return;
      try {
        const sig = signature();
        if (sig !== lastSaved) {
          await idbPut(SAVED, { savedAt: Date.now(), files: walk("/dark", {}, false) });
          lastSaved = sig;
        }
      } catch (e) {
        console.warn("[dark] could not keep this tab's work: " + e);
      }
    });
    return queue;
  }

  // Put back what an earlier visit kept, before Boot. True when there was something.
  async function restore() {
    try {
      const saved = await idbGet(SAVED);
      if (!saved || !saved.files) return false;
      for (const [path, bytes] of Object.entries(saved.files)) {
        let dir = "";
        for (const part of path.split("/").slice(1, -1)) { dir += "/" + part; try { fs().mkdir(dir); } catch (e) {} }
        fs().writeFile(path, bytes);
      }
      return true;
    } catch (e) {
      console.warn("[dark] could not read this tab's kept work: " + e);
      return false;
    }
  }

  // After Boot: act on what it did with the restored store, then keep saving.
  async function keepSaving(restored) {
    const outcome = restored ? await invoke("RestoreOutcome") : "";
    restoreOutcome = outcome;
    // Saved by a newer release than this page runs (an older deploy, or a cached page). The newer
    // build wants that store as it was, so this tab leaves it alone and saves nothing.
    if (outcome.startsWith("newer")) { console.log("[dark] kept work in this browser: " + outcome); return outcome; }
    try {
      if (outcome.startsWith("refused")) {
        // Never overwritten: set aside under a name of its own, for whoever can read it later.
        await idbPut(SAVED + "-refused-" + new Date().toISOString(), await idbGet(SAVED));
        await idbDelete(SAVED);
      } else if (outcome === "upgraded") {
        // One copy of the store as the older build left it, the way the desktop backs up before an upgrade.
        await idbPut(SAVED + "-before-upgrade", await idbGet(SAVED));
      }
    } catch (e) { console.warn("[dark] could not set aside this tab's earlier work: " + e); }
    persisting = true;
    // A store this build already reconciled is exactly what is kept; no need to write it straight back.
    lastSaved = outcome === "kept" ? signature() : null;
    await save();
    // The workbench runs as one long command, so saving after commands alone would miss everything
    // done inside it.
    setInterval(save, 15000);
    document.addEventListener("visibilitychange", () => { if (document.visibilityState === "hidden") save(); });
    if (outcome) console.log("[dark] kept work in this browser: " + outcome);
    return outcome;
  }

  async function boot(onStatus = () => {}) {
    onStatus("loading the Darklang runtime");
    for (let i = 0; i < 80 && !(typeof Blazor !== "undefined" && Blazor.start); i++) await sleep(250);
    if (!(typeof Blazor !== "undefined" && Blazor.start)) throw new Error("_framework/blazor.webassembly.js did not load");
    await Blazor.start();
    // The JS-interop dispatcher isn't ready the instant Blazor.start() resolves.
    let ready = false;
    for (let i = 0; i < 120 && !ready; i++) { try { await invoke("Ready"); ready = true; } catch (e) { await sleep(500); } }
    if (!ready) throw new Error("runtime did not become ready");
    const restored = await restore();
    onStatus(restored ? "loading your work from this browser" : "loading the package store");
    // store.json names the store file and its inflated size (the one-shot brotli decoder needs it).
    const manifest = await (await fetch(new URL("store.json", document.baseURI))).json();
    await invoke("Boot", new URL(manifest.file, document.baseURI).href, manifest.size);
    await keepSaving(restored);
  }

  async function run(argv) {
    const res = await invoke("RunCommand", argv);
    save();
    const i = res.lastIndexOf("\n[exit ");
    return { output: res.slice(0, i), code: res.slice(i + 7, -1) };
  }

  const stripAnsi = (s) => s.replace(/\x1b\[[0-9;]*[A-Za-z]/g, "");

  function splitArgv(text) {
    const argv = []; let cur = ""; let quoted = false;
    for (const ch of text) {
      if (ch === '"') { quoted = !quoted; continue; }
      if (!quoted && /\s/.test(ch)) { if (cur) { argv.push(cur); cur = ""; } continue; }
      cur += ch;
    }
    if (cur) argv.push(cur);
    return argv;
  }

  // `restored`: what Boot did with work kept from an earlier visit ("", "kept", "upgraded", or
  // "refused: <why>"). `save`: keep the tab's work now, rather than at the next command.
  return { invoke, boot, run, save, restored: () => restoreOutcome, stripAnsi, splitArgv, takesTheScreen: (argv) => screenCommands.has(argv[0]) };
})();
