#!/bin/bash

DIR="${1:-.}"

# List of directories to ignore
IGNORE_DIRS=("_later" "_scripts")

# Build find prune arguments
PRUNE_ARGS=""
for dir in "${IGNORE_DIRS[@]}"; do
    PRUNE_ARGS="$PRUNE_ARGS -path */$(printf '%q' "$dir") -prune -o"
done

# Calculate total size in bytes (ignoring specified directories)
total_size=$(eval "find \"$DIR\" $PRUNE_ARGS -type f -exec stat -c%s {} + 2>/dev/null" | awk '{sum+=$1} END {print sum}')

# Count total number of files (ignoring specified directories)
total_files=$(eval "find \"$DIR\" $PRUNE_ARGS -type f -print 2>/dev/null" | wc -l)

# Count total words (using wc on all files, ignoring binary files and specified directories)
total_words=$(eval "find \"$DIR\" $PRUNE_ARGS -type f -exec file {} \; 2>/dev/null" | \
    grep -E "text|ASCII|UTF" | \
    cut -d: -f1 | \
    xargs wc -w 2>/dev/null | \
    tail -n1 | \
    awk '{print $1}')

# Handle case where no text files found
if [ -z "$total_words" ]; then
    total_words=0
fi

echo "Directory: $DIR"
echo "Total size: ${total_size:-0} bytes"
echo "Total files: $total_files"
echo "Total words: $total_words"