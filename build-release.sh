#!/bin/bash
# usage: build-release.sh <label> [clone]  -- the release musl CLI of the clone's HEAD, into bins/<label>/darklang
# Same steps as CI's seed-db + build-cli-linux-musl (x64 only): dev build, export the seed and
# package-ref hashes, then AOT-publish linux-musl-x64 in the CI image. The AOT step takes a box-wide slot.
set -euo pipefail
B=$(cd "$(dirname "$0")" && pwd); label=$1; C=${2:-$HOME/code/dark/bench-loop}; M=/tmp/bench-loop-musl
cd "$C"
[ -z "$(git status --porcelain --untracked-files=no)" ] || { echo "clone has uncommitted changes"; exit 1; }
sha=$(git rev-parse HEAD)
echo "$(date +%T) $label: $(git log --oneline -1)"
scripts/dev/build < /dev/null 2>&1 | tail -2
scripts/dev/exec scripts/run-local-exec export-seed rundir/seed.db < /dev/null 2>&1 | tail -1
bash "$B/musl-image.sh" "$C"
mkdir -p $M/nuget
[ -d $M/app/.git ] || git clone -q --no-hardlinks "$C" $M/app
git -C $M/app fetch -q "$C" "$sha" && git -C $M/app checkout -qf --detach "$sha"
cp backend/src/LibExecution/package-ref-hashes.txt $M/app/backend/src/LibExecution/
cp rundir/seed.db $M/app/seed.db
export DARK_SUITE_SLOTS=${DARK_SUITE_SLOTS:-4} SUITE_SLOT_DIR=$HOME/.cache/darklang/suite-slots
. "$B/_suite-slot"
[ -z "${BENCH_YIELD:-}" ] || "$B/yield-gate.sh"
suite_slot_take "bench-loop aot $label"
echo "$(date +%T) slot taken, AOT"
docker run --rm -v $M/app:/root/app -v $M/nuget:/root/.nuget bench-loop-musl:1 bash -c '
  set -e; cd /root/app; export IN_DEV_CONTAINER=true PATH="/root/zig:$PATH"
  trap "chown -R 1000:1000 /root/app /root/.nuget" EXIT
  ./scripts/build/_dotnet-wrapper restore --locked-mode fsdark.sln > /tmp/restore.log 2>&1 || { tail -30 /tmp/restore.log; exit 1; }
  [ -f backend/src/Cli/lib/libe_sqlite3-linux-musl-x64.a ] || ./scripts/build/build-sqlite.sh --runtimes=linux-musl-x64 > /tmp/sqlite.log 2>&1 || { tail -30 /tmp/sqlite.log; exit 1; }
  ./scripts/build/build-release-cli-exes.sh --mode=aot --seed=seed.db --runtimes=linux-musl-x64 > /tmp/aot.log 2>&1 || { tail -40 /tmp/aot.log; exit 1; }
  tail -3 /tmp/aot.log'
out=$(ls $M/app/clis/darklang-alpha-${sha:0:10}-linux-musl-x64)
mkdir -p "$B/bins/$label"
cp "$out" "$B/bins/$label/darklang.new" && mv "$B/bins/$label/darklang.new" "$B/bins/$label/darklang"
[ -f "$out.mode" ] && cp "$out.mode" "$B/bins/$label/darklang.mode"
echo "$sha" > "$B/bins/$label/commit"
echo "$(date +%T) built $label from ${sha:0:10}: $B/bins/$label/darklang"
