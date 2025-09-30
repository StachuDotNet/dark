### Migration Strategy: From File Explorer to ViewContainer

### Phase 1: Remove Package "Files" from File Explorer

**Current Wrong Approach (eliminate this):**
```
File Explorer
├── packages/
│   ├── darklang/
│   │   ├── stdlib/
│   │   │   └── list.dark    # ❌ Not actually a file
│   │   └── http.dark        # ❌ Confusing to users
└── frontend/
    └── index.html
```

**New Correct Approach (move to this):**
```
File Explorer                Darklang ViewContainer
├── frontend/                ├── 📦 Packages
│   ├── styles.css           │   ├── 🏢 Darklang
│   └── index.html           │   │   ├── 📁 Stdlib
├── docs/                    │   │   │   └── 🔧 List.map
└── README.md                │   │   └── 📁 Http
                             │   │       └── 🔧 get
                             ├── 📝 Patches
                             └── 🎯 Sessions
```