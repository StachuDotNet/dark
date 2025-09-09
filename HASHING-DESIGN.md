# Hash-Based Artifact References: Design Specification

## Overview

Darklang artifacts (types, functions, values) should be content-addressed using cryptographic hashes rather than UUIDs. This enables:
- Content verification and integrity checking
- Deterministic artifact identification
- Efficient caching and deduplication
- Immutable artifact references

## Hash Algorithm Selection

### Primary: BLAKE3-256
- **Rationale**: Fast, secure, parallelizable, no length extension attacks
- **Output**: 256 bits (32 bytes)
- **Representation**: 64 hex characters

### Alternative Considerations
| Algorithm | Speed | Security | Output Size | Use Case |
|-----------|-------|----------|-------------|----------|
| BLAKE3-256 | Fastest | High | 256 bits | Primary choice |
| SHA-256 | Moderate | High | 256 bits | Compatibility fallback |
| XXH3-128 | Very Fast | Non-crypto | 128 bits | Local caching only |
| SHA3-256 | Slow | Highest | 256 bits | If NIST compliance needed |

## Content Canonicalization

### What to Hash

Hash the **canonical semantic representation**, not the serialized binary:

1. **Types**: Structure definition (fields, cases, type parameters)
2. **Functions**: Parameters, return type, body AST, type parameters
3. **Values**: Body expression AST

### Canonical Form Rules

```fsharp
type CanonicalForm = {
  // Semantic content only
  owner: string
  modules: string list
  name: string
  definition: CanonicalDefinition
  // Excluded: IDs, timestamps, descriptions, deprecation status
}
```

### Self-Reference Handling

For recursive types/functions:
1. Replace self-references with placeholder during hash calculation
2. Use semantic marker: `SelfRef(owner.modules.name)`
3. Compute hash with placeholder, then substitute actual hash

## Hash Storage and Representation

### Database Storage
```sql
CREATE TABLE package_artifacts (
  hash BYTEA PRIMARY KEY,           -- 32 bytes for BLAKE3-256
  algorithm SMALLINT NOT NULL,      -- 1=BLAKE3-256, 2=SHA-256
  owner TEXT NOT NULL,
  modules TEXT NOT NULL,
  name TEXT NOT NULL,
  content_type SMALLINT NOT NULL,   -- 1=Type, 2=Function, 3=Value
  canonical_json TEXT,              -- For debugging/recomputation
  created_at TIMESTAMPTZ NOT NULL
);
```

### In-Memory Representation
```fsharp
type HashAlgorithm = 
  | BLAKE3_256 = 1
  | SHA_256 = 2

type ArtifactHash = {
  algorithm: HashAlgorithm
  digest: byte[]  // Full digest
}

type Hash = 
  | Hash of ArtifactHash
  member this.ToString() = 
    // Full hex representation
  member this.ToShortId() = 
    // Crockford base32, 12 chars
```

## Short IDs for UI/CLI

### Encoding: Crockford Base32
- **Characters**: `0123456789ABCDEFGHJKMNPQRSTVWXYZ`
- **Benefits**: Case-insensitive, no ambiguous characters (I/L/O/U)
- **Length**: 12 characters standard (60 bits)

### Generation Process
1. Take first 60 bits of hash
2. Encode as Crockford base32
3. Prefix uniqueness check in current scope
4. Extend if collision (rare)

### Example Transformations
```
Full hash:    3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c
Short ID:     7B9CW3KH4XNP
Display:      PackageFn.Package(7B9CW3KH4XNP)
Tooltip:      "Full hash: 3b4c5d6e7f..."
```

## Collision Analysis

### Birthday Paradox Probabilities

For BLAKE3-256 (2^256 space):

| Artifacts | Collision Probability |
|-----------|----------------------|
| 10^6 | ~10^-69 |
| 10^9 | ~10^-60 |
| 10^12 | ~10^-51 |

For 12-char short IDs (60 bits):

| Artifacts | Collision Probability |
|-----------|----------------------|
| 1,000 | ~0.0000004% |
| 10,000 | ~0.004% |
| 100,000 | ~0.4% |
| 1,000,000 | ~36% |

**Recommendation**: Use full hashes internally, short IDs only for display.

## Namespacing and Versioning

### Hash Namespacing
```fsharp
let computeHash (artifact: Artifact) =
  let prefix = 
    match artifact with
    | Type _ -> "darklang:type:v1:"
    | Function _ -> "darklang:fn:v1:"
    | Value _ -> "darklang:val:v1:"
  
  BLAKE3.hash(prefix + canonicalJson)
```

### Version Migration
- Include version in namespace prefix
- New versions get new prefixes
- Enables parallel existence of v1/v2 artifacts

## Migration Strategy

### Phase 1: Dual-Write (Current)
1. Add hash columns to existing tables
2. Calculate hashes on insert
3. Continue using UUIDs for lookups

### Phase 2: Dual-Read
1. Support both UUID and hash lookups
2. Backfill hashes for existing artifacts
3. Update references incrementally

### Phase 3: Hash-Primary
1. Use hashes for new references
2. Maintain UUID mapping table for legacy
3. Migrate UI/CLI to use short IDs

### Phase 4: Hash-Only
1. Drop UUID columns
2. Archive UUID mapping table
3. Complete migration

## Implementation Checklist

### Immediate Requirements
- [ ] Replace SHA-256 with BLAKE3-256
- [ ] Fix dual hashing system (content vs name)
- [ ] Calculate hashes at parse time
- [ ] Implement Crockford base32 short IDs
- [ ] Add collision detection on insert

### Testing Requirements
- [ ] Hash determinism across platforms
- [ ] Canonical form stability
- [ ] Short ID uniqueness
- [ ] Migration data integrity
- [ ] Performance benchmarks

### Monitoring
- [ ] Hash calculation time percentiles
- [ ] Short ID collision rate
- [ ] Cache hit rates
- [ ] Migration progress

## Security Considerations

1. **Hash Algorithm**: BLAKE3-256 is cryptographically secure
2. **Timing Attacks**: Not applicable (public data)
3. **Collision Attacks**: Practically impossible with 256-bit space
4. **Canonicalization Attacks**: Strict canonical form prevents variants

## Performance Targets

- Hash calculation: < 1ms for typical artifact
- Short ID generation: < 10μs
- Hash lookup: O(1) with index
- Collision check: < 100μs

## Future Extensions

1. **Content Deduplication**: Same artifact uploaded twice = same hash
2. **Merkle Trees**: For package dependency verification
3. **IPFS Integration**: Content-addressed distributed storage
4. **Signed Artifacts**: Add signature alongside hash for authenticity