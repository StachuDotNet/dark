#!/usr/bin/env bash
# CI: decide whether this build gets a preview, upload it, tell the PR.
#
#   preview/ci-deploy.sh [site-dir]     upload; comment on the PR; on main, clean up
#   preview/ci-deploy.sh --decide       print the path this build would go to, or exit 3
#                                       (build-wasm halts on that before the publish)
#
# A build is deployed when it is main, a v* tag, or a PR (CIRCLE_PULL_REQUEST) whose author
# is on PREVIEW_AUTHORS; see the README for why. Needs PREVIEW_FLY_API_TOKEN, and
# PREVIEW_GITHUB_TOKEN for the comment. Without them it says so and exits 0.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="${PREVIEW_REPO:-darklang/dark}"
ALLOWED="${PREVIEW_AUTHORS:-StachuDotNet OceanOak pbiggar}"
DECIDE=false; [[ "${1:-}" == "--decide" ]] && { DECIDE=true; shift; }

branch="${CIRCLE_TAG:-${CIRCLE_BRANCH:-$(git branch --show-current)}}"
sha="${CIRCLE_SHA1:-$(git rev-parse HEAD)}"
pr=""
if   [[ "$branch" == v* ]];                    then path="tag"
elif [[ "$branch" == "main" ]];                then path="main"
elif [[ -n "${CIRCLE_PULL_REQUEST:-}" ]];      then pr="${CIRCLE_PULL_REQUEST##*/}"; path="pulls/$pr"
else echo "no pull request for $branch; not deploying a preview"; $DECIDE && exit 3 || exit 0
fi
$DECIDE && { echo "$path"; exit 0; }

[[ -n "${PREVIEW_FLY_API_TOKEN:-}" ]] || { echo "PREVIEW_FLY_API_TOKEN is not set; not deploying a preview"; exit 0; }
export FLY_API_TOKEN="$PREVIEW_FLY_API_TOKEN" GITHUB_TOKEN="${PREVIEW_GITHUB_TOKEN:-}"
gh() { curl -sS --fail ${GITHUB_TOKEN:+-H "Authorization: Bearer $GITHUB_TOKEN"} "$@"; }

title=""; author=""
if [[ -n "$pr" ]]; then
  meta=$(gh "https://api.github.com/repos/$REPO/pulls/$pr")
  author=$(jq -r .user.login <<<"$meta"); title=$(jq -r .title <<<"$meta")
  grep -qwF -- "$author" <<<"$ALLOWED" \
    || { echo "PR #$pr is by $author, not in PREVIEW_AUTHORS ($ALLOWED); not deploying a preview"; exit 0; }
fi

url=$(PREVIEW_BRANCH="$branch" PREVIEW_SHA="$sha" PREVIEW_PR="$pr" PREVIEW_TITLE="$title" PREVIEW_AUTHOR="$author" \
      "$HERE/host.sh" upload "$path" "$@" | tee /dev/stderr | tail -n 1)
[[ "$path" != "main" ]] || "$HERE/host.sh" cleanup || echo "cleanup failed; the next main deploy retries"
[[ -n "$pr" && -n "$GITHUB_TOKEN" ]] || exit 0

# One comment per PR: find ours by the marker and edit it, else create it.
marker="<!-- dark-preview -->"
body="$marker
Browser CLI built from this branch, at ${sha:0:7}: $url

\`cli.html?cmd=<argv>\` runs a command first, \`?fn=<name>\` lands on an item, \`eval.html?e=<expr>\` is an expression box. Nothing persists between reloads."
existing=$(gh "https://api.github.com/repos/$REPO/issues/$pr/comments?per_page=100" \
  | jq -r --arg m "$marker" 'map(select(.body | startswith($m)))[0].id // empty')
payload=$(jq -n --arg body "$body" '{body:$body}')
if [[ -n "$existing" ]]; then gh -X PATCH "https://api.github.com/repos/$REPO/issues/comments/$existing" -d "$payload" >/dev/null
else gh -X POST "https://api.github.com/repos/$REPO/issues/$pr/comments" -d "$payload" >/dev/null; fi
echo "PR comment ${existing:+updated}${existing:-created}"
