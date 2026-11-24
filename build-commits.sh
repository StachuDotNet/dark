#!/bin/bash
# usage: build-commits.sh <branch> [clone] -- release binaries for the branch's base (c0) and each commit (c1..cN)
# into bins/c<k>/, one after another. Leaves the clone back on <branch>.
set -euo pipefail
B=$(cd "$(dirname "$0")" && pwd); branch=$1; C=${2:-$HOME/code/dark/bench-loop}
cd "$C"
base=$(git merge-base "$branch" upstream/main)
commits=("$base" $(git rev-list --reverse "$base..$branch"))
for k in "${!commits[@]}"; do
  sha=${commits[$k]}
  if [ "$(cat "$B/bins/c$k/commit" 2>/dev/null)" = "$sha" ]; then echo "c$k already built at ${sha:0:10}"; continue; fi
  git checkout -q --detach "$sha"
  bash "$B/build-release.sh" "c$k" "$C"
done
git checkout -q "$branch"
for k in "${!commits[@]}"; do echo "c$k $(git log --oneline -1 ${commits[$k]})"; done
