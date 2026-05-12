#!/bin/bash
. ./scripts/devcontainer/_assert-in-container "$0" "$@"

# Builds SQLite as a static archive (libe_sqlite3-<rid>.a) per target runtime.
#
# Consumed by the NativeAOT-published CLI: backend/src/Cli/Cli.fsproj wires
# these archives via <DirectPInvoke Include="e_sqlite3" /> +
# <NativeLibrary Include="lib/libe_sqlite3-$(RuntimeIdentifier).a" />, so the
# AOT-published binary has all SQLite symbols statically resolved at link time
# and doesn't need libe_sqlite3.so on the user's filesystem.
#
# The DllImport name in SQLitePCLRaw.provider.e_sqlite3 is "e_sqlite3"; the
# archive base name matches that so DirectPInvoke binds the import. The .a
# suffix is by Linux/Apple convention — Windows static libs would be .lib,
# but we don't currently cross-compile the windows variants from this script
# (the CLI's windows AOT build path is not active yet).
#
# Note: the list of runtime targets here must stay in sync with
# - backend/src/Cli/Cli.fsproj (per-RID <NativeLibrary> items)
# - scripts/build/build-release-cli-exes.sh
# - backend/src/LibTreeSitter/LibTreeSitter.fsproj
#
# Uses zig cc for cross-compile (single toolchain, no per-target sysroot
# wrangling), matching the pattern in build-tree-sitter.sh.

set -euo pipefail

cd ~/

# SQLite amalgamation (single-file C source). Public domain. The version pin
# is intentional — bumping it changes the on-disk database format only across
# major versions, and we want reproducible builds.
sqlite_version="3460000"
sqlite_url="https://www.sqlite.org/2024/sqlite-amalgamation-${sqlite_version}.zip"
sqlite_sha256="712a7d09d2a22652fb06a49af516e051979a3984adb067da86760e60ed51a7f5"
sqlite_dir="sqlite-amalgamation-${sqlite_version}"

output_base_dir="app/backend/src/Cli/lib"

# If every artifact already exists, skip (matches the cache check in
# build-tree-sitter.sh).
all_present=true
for rid in linux-x64 linux-musl-x64 linux-arm64 linux-arm osx-x64 osx-arm64; do
  if [[ ! -f "$output_base_dir/libe_sqlite3-${rid}.a" ]]; then
    all_present=false
    break
  fi
done
if [[ "$all_present" == "true" ]]; then
  echo "SQLite static archives already present in $output_base_dir. Skipping."
  exit 0
fi

# Fetch + verify amalgamation
if [[ ! -d "$sqlite_dir" ]]; then
  curl -sSL -O "$sqlite_url"
  echo "$sqlite_sha256  sqlite-amalgamation-${sqlite_version}.zip" | sha256sum -c -
  unzip -q "sqlite-amalgamation-${sqlite_version}.zip"
fi

mkdir -p "$output_base_dir"

# Compile flags. We need to satisfy the symbol set that
# SQLitePCLRaw.provider.e_sqlite3 imports via [DllImport("e_sqlite3")] —
# under NativeAOT + DirectPInvoke those become hard link references, so any
# DllImport whose target is missing breaks the link even if F# never calls
# it. SQLitePCL's bindings include load_extension and the column-metadata
# functions unconditionally, so we have to keep them in the build.
#
# Notable choices:
#   ENABLE_COLUMN_METADATA — needed for sqlite3_column_{database,origin,table}_name
#                            which the provider binds unconditionally.
#   load extension is enabled at COMPILE time (symbols present) but disabled
#     at RUNTIME by default; we never call sqlite3_enable_load_extension(db,1),
#     so we get the security posture for free.
#   THREADSAFE=1 — full mutex, matches what SQLitePCLRaw ships.
#   DQS=0 — reject double-quoted string literals, catches SQL bugs early.
#   DEFAULT_MEMSTATUS=0 — drops per-connection memstats overhead.
#   DO NOT add SQLITE_OMIT_AUTOINIT — SQLitePCLRaw's provider doesn't call
#     sqlite3_initialize() explicitly; it relies on SQLite's default
#     auto-init-on-first-API-call. With OMIT_AUTOINIT we get a segfault on
#     the first sqlite3_open call. Tested 2026-05-12.
sqlite_cflags="-Os \
  -DSQLITE_THREADSAFE=1 \
  -DSQLITE_ENABLE_COLUMN_METADATA \
  -DSQLITE_ENABLE_MATH_FUNCTIONS \
  -DSQLITE_DEFAULT_MEMSTATUS=0 \
  -DSQLITE_DQS=0"

sqlite_src="$sqlite_dir/sqlite3.c"
sqlite_inc="-I $sqlite_dir"

# Build one static archive for the given zig target + .NET runtime identifier.
# Two-step: zig cc -c → .o, then zig ar rcs → .a. Zig bundles llvm-ar so we
# get target-correct archives regardless of host.
build_one() {
  local zig_target="$1"
  local rid="$2"

  local objfile="$output_base_dir/sqlite3-${rid}.o"
  local arfile="$output_base_dir/libe_sqlite3-${rid}.a"

  echo "  → $rid"
  "$HOME/zig/zig" cc -target "$zig_target" $sqlite_cflags -fPIC -c \
    $sqlite_inc "$sqlite_src" -o "$objfile"
  "$HOME/zig/zig" ar rcs "$arfile" "$objfile"
  rm -f "$objfile"
}

echo "Compiling SQLite static archives via zig cc..."
build_one x86_64-linux-gnu       linux-x64       &
build_one x86_64-linux-musl      linux-musl-x64  &
build_one aarch64-linux-gnu      linux-arm64     &
build_one arm-linux-gnueabihf    linux-arm       &
build_one x86_64-macos-none      osx-x64         &
build_one aarch64-macos-none     osx-arm64       &
wait

# Clean up amalgamation source (we re-download next time; keeps the repo
# tree clean and avoids accidentally committing it).
rm -rf "$sqlite_dir" "sqlite-amalgamation-${sqlite_version}.zip"

echo ""
echo "Done. Artifacts:"
ls -lh "$output_base_dir"/libe_sqlite3-*.a
