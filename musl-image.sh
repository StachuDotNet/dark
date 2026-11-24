#!/bin/bash
# one-time: the CI musl job's image (stock SDK + apk packages + pinned toolchain) as bench-loop-musl:1
set -euo pipefail
C=${1:-$HOME/code/dark/bench-loop}
docker image inspect bench-loop-musl:1 > /dev/null 2>&1 && { echo "bench-loop-musl:1 exists"; exit 0; }
img=mcr.microsoft.com/dotnet/sdk@sha256:52556c0fb9d0f609ae9aeab9efeeba96f4acc2f7038f7696f2235ccbdbb8e290
docker rm -f bench-loop-musl-setup 2> /dev/null || true
docker run --name bench-loop-musl-setup -v "$C/scripts:/src/scripts:ro" -v "$C/backend/global.json:/src/backend/global.json:ro" $img sh -c '
  apk add --no-cache bash git openssh-client curl clang build-base zlib-dev openssl icu-libs cmake openssl-dev openssl-libs-static tar xz sqlite lld llvm > /dev/null &&
  bash /src/scripts/build/install-release-toolchain linux-musl-x64 /usr/share/dotnet /root/zig && dotnet --version &&
  git config --global --add safe.directory "*"'
docker commit bench-loop-musl-setup bench-loop-musl:1 > /dev/null
docker rm bench-loop-musl-setup > /dev/null
echo "made bench-loop-musl:1"
