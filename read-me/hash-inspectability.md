# Hash Inspectability (future PR)

The hashing process should be inspectable — builtins or CLI commands
that let you see:
- How an item was hashed (what canonical bytes were produced)
- The intermediate values (dependency hashes, SCC group membership)
- Why two items have different hashes (diff the canonical forms)

This is a debugging/development tool, not part of the core hashing PR.
