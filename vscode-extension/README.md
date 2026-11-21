# Darklang for VS Code

Diagnostics, hover and go-to-definition for Darklang, from `dark lsp`.

- Install `dark` and make sure it's on your `PATH`, or set `darklang.path`.
- Open a `.dark` file. The extension starts `dark lsp` and stops it when VS Code closes.
- Go to the definition of a package function and it opens from the store as a
  `darkfs:/Owner/Module/name.fn.dark` document. That document reloads when the store changes.

## Building

```
cd vscode-extension
npm ci
npm run build      # type-checks and bundles to out/extension.js
npm run package    # darklang-<version>.vsix
```

In VS Code at the repo root, "Run the VS Code extension" (F5) opens a development window with it. Run `npm run build` first.
