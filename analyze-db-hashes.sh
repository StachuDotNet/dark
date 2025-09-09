#!/bin/bash

echo "=== Database Hash Analysis ==="
echo "Date: $(date)"
echo

# Count artifacts
echo "=== Artifact Counts ==="
echo "Types:     $(sqlite3 rundir/data.db 'SELECT COUNT(*) FROM package_types_v0 WHERE hash IS NOT NULL;')"
echo "Values:    $(sqlite3 rundir/data.db 'SELECT COUNT(*) FROM package_values_v0 WHERE hash IS NOT NULL;')"
echo "Functions: $(sqlite3 rundir/data.db 'SELECT COUNT(*) FROM package_functions_v0 WHERE hash IS NOT NULL;')"
echo

# Extract all hashes
echo "=== Extracting hashes for analysis ==="
sqlite3 rundir/data.db "
  SELECT hash FROM package_types_v0 WHERE hash IS NOT NULL
  UNION ALL
  SELECT hash FROM package_values_v0 WHERE hash IS NOT NULL
  UNION ALL
  SELECT hash FROM package_functions_v0 WHERE hash IS NOT NULL
" > /tmp/all_hashes.txt

total=$(wc -l < /tmp/all_hashes.txt)
unique=$(sort -u /tmp/all_hashes.txt | wc -l)
echo "Total hashes:  $total"
echo "Unique hashes: $unique"
echo "Duplicates:    $((total - unique))"
echo

# Check for collisions
echo "=== Checking for hash collisions ==="
sort /tmp/all_hashes.txt | uniq -d > /tmp/duplicate_hashes.txt
if [ -s /tmp/duplicate_hashes.txt ]; then
    echo "Found duplicate hashes:"
    cat /tmp/duplicate_hashes.txt
else
    echo "No duplicate hashes found (no collisions)"
fi
echo

# Analyze hash prefixes
echo "=== Minimum unique prefix length ==="
for len in {1..64}; do
    cut -c1-$len /tmp/all_hashes.txt | sort | uniq -d > /tmp/prefix_dups.txt
    if [ ! -s /tmp/prefix_dups.txt ]; then
        echo "All hashes are unique at $len character prefix"
        break
    fi
done
echo

# Short ID analysis (first 12 hex chars = 6 bytes = ~48 bits)
echo "=== Short ID collision analysis (hex prefixes) ==="
for len in 8 12 16 20; do
    total_ids=$(wc -l < /tmp/all_hashes.txt)
    unique_ids=$(cut -c1-$len /tmp/all_hashes.txt | sort -u | wc -l)
    collisions=$((total_ids - unique_ids))
    rate=$(awk "BEGIN {printf \"%.4f\", $collisions * 100 / $total_ids}")
    echo "$len chars: $collisions collisions ($rate%)"
done
echo

# Sample hashes
echo "=== Sample hashes ==="
echo "First 5 function hashes:"
sqlite3 rundir/data.db "
  SELECT substr(hash, 1, 12) as short_id, 
         owner || '.' || modules || '.' || name as full_name,
         hash
  FROM package_functions_v0 
  WHERE hash IS NOT NULL 
  LIMIT 5;
" | sed 's/|/ | /g'
echo

# Hash distribution
echo "=== Hash character distribution (first byte) ==="
cut -c1-2 /tmp/all_hashes.txt | sort | uniq -c | sort -rn | head -10
echo

# Cleanup
rm -f /tmp/all_hashes.txt /tmp/duplicate_hashes.txt /tmp/prefix_dups.txt

echo "=== Analysis complete ==="