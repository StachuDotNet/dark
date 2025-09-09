# Hash-Based Artifact References: Empirical Report

## Data Collection Summary

**Date**: 2025-09-03  
**Branch**: ocean/ref-by-hash  
**Environment**: Darklang monorepo

## Artifact Inventory

### Source Files (.dark)
- **Files analyzed**: 229 .dark files in packages/
- **Estimated artifacts**: 6,605 total
  - Types: 1,223
  - Functions: 4,992
  - Values: 390

### Database (rundir/data.db)
- **Actual stored artifacts**: 2,230 total
  - Types: 468
  - Values: 143
  - Functions: 1,619

## Collision Analysis Results

### Full Hash Collisions (SHA-256, 256-bit)
- **Observed collisions**: 0 out of 2,230 hashes
- **Collision rate**: 0.00%
- **Result**: No collisions detected with current data set

### Theoretical Collision Probabilities

| Artifacts | 32-bit | 64-bit | 128-bit | 256-bit |
|-----------|--------|--------|---------|---------|
| 1,000 | 0.012% | 2.7×10⁻¹⁴ | ~0 | ~0 |
| 10,000 | 1.16% | 2.7×10⁻¹² | ~0 | ~0 |
| 100,000 | 68.8% | 2.7×10⁻¹⁰ | ~0 | ~0 |
| 1,000,000 | ~100% | 2.7×10⁻⁸ | ~0 | ~0 |
| 10,000,000 | ~100% | 0.00027% | ~0 | ~0 |

### Short ID Analysis

#### Hex Prefix Uniqueness (Real Data)
| Prefix Length | Collisions | Rate |
|---------------|------------|------|
| 6 chars | 0 | 0.00% |
| 8 chars | 0 | 0.00% |
| 12 chars | 0 | 0.00% |
| 16 chars | 0 | 0.00% |
| 20 chars | 0 | 0.00% |

**Finding**: All 2,230 hashes are unique with just a 6-character hex prefix

#### Crockford Base32 Short IDs (Simulated 100k artifacts)
| ID Length | Collisions | Rate |
|-----------|------------|------|
| 6 chars | 7 | 0.007% |
| 8 chars | 0 | 0.000% |
| 10 chars | 0 | 0.000% |
| 12 chars | 0 | 0.000% |

**Recommendation**: Use 12-character Crockford base32 for UI display (provides ~60 bits of entropy)

## Performance Benchmarks

### SHA-256 Hashing Performance
| Artifact Size | Time (10k ops) | Operations/sec |
|---------------|----------------|----------------|
| Small (100 bytes) | 11.6 ms | 863,163 |
| Medium (1 KB) | 12.8 ms | 778,810 |
| Large (10 KB) | 56.8 ms | 176,030 |
| XLarge (100 KB) | 494.0 ms | 20,242 |

**Finding**: SHA-256 is more than fast enough for typical artifacts (<1ms per hash)

## Hash Distribution Analysis

### First Byte Distribution (Real Data)
- **Entropy**: Good distribution across byte space
- **Top byte values**: 0x1d (19 occurrences), 0xea (16), 0xc6 (16), 0x83 (16)
- **Coverage**: Well-distributed across 256 possible values
- **Standard deviation**: Acceptable variance indicating good randomness

### Sample Real Hashes
```
955af4bf730d... | Darklang.Internal.Test.parseTest
594fb3496f67... | Darklang.Internal.Test.parseSingleTestFromFile
9f49fa7e37ca... | Darklang.Cli.Tests.runWithCommand
f54a4e751076... | Darklang.Cli.Tests.testHelpCommand
b7a7d174b723... | Darklang.Cli.Tests.testVersionCommand
```

## Storage Requirements

### For Current Dataset (2,230 artifacts)

| Algorithm | Bytes/Artifact | Total Storage |
|-----------|----------------|---------------|
| SHA-256 | 32 | 69.7 KB |
| BLAKE3-256 | 32 | 69.7 KB |
| XXH3-128 | 16 | 34.8 KB |

### Projected for 100,000 artifacts

| Algorithm | Bytes/Artifact | Total Storage |
|-----------|----------------|---------------|
| SHA-256 | 32 | 3.05 MB |
| BLAKE3-256 | 32 | 3.05 MB |
| XXH3-128 | 16 | 1.53 MB |

## Key Findings

1. **No Collisions**: Zero hash collisions in production data (2,230 artifacts)
2. **Efficient Prefixes**: 6 hex characters sufficient for current uniqueness
3. **Performance**: Sub-millisecond hashing for typical artifacts
4. **Distribution**: Good entropy in hash outputs
5. **Storage**: Minimal overhead (~32 bytes per artifact)

## Recommendations Based on Data

### Immediate Implementation
1. **Algorithm**: Continue with SHA-256 (proven, no collisions, fast enough)
2. **Short IDs**: Implement 12-char Crockford base32 for display
3. **Storage**: Store full 256-bit hash in database
4. **Indexing**: Create index on first 8 bytes for efficient lookups

### Future Considerations
1. **BLAKE3**: Consider migration when artifact count exceeds 100k (3x faster)
2. **Monitoring**: Add collision detection logging
3. **Prefix Length**: Re-evaluate when artifact count exceeds 10k
4. **Cache**: Implement hash caching for frequently accessed artifacts

## Migration Impact

### Current State
- 2,230 artifacts need hash migration
- No existing collisions to resolve
- Clean migration path available

### Performance Impact
- Initial migration: ~2.3 seconds (at 1ms per hash)
- Ongoing overhead: <1ms per new artifact
- Lookup performance: O(1) with proper indexing

## Conclusion

The hash-based system is working well in practice with:
- Zero collisions on real data
- Excellent performance characteristics
- Minimal storage overhead
- Good distribution properties

The main issue identified is the dual hashing system (content vs name-based), which should be unified to ensure consistency.