#!/usr/bin/env bash

# Composes the GitHub release body from the exported release notes.
#
# The notes are plain text and a release body is rendered as GitHub Flavored
# Markdown in the repository's context, so the two disagree wherever the text
# holds something Markdown reads as markup. Measured on the 1.0.0-dev.3 body:
# every generic argument was parsed as an HTML tag and dropped, leaving "the
# nullable and List forms", and "@total" and "@name" - parameter names in a
# sentence about Dapper - became links to the accounts that own them, which put
# two strangers on the release page as contributors. A fenced block renders the
# text verbatim and is the whole fix; nothing inside one is markup.

set -euo pipefail

notes=$1
body=$2

# The fence has to be longer than the longest run of backticks in the text, or
# the text closes it early and the tail renders as Markdown after all.
longest=$(grep -o '`\+' "$notes" | awk '{ if (length($0) > n) { n = length($0) } } END { print n + 0 }' || true)
length=3
if [[ ${longest:-0} -ge 3 ]]; then
  length=$((longest + 1))
fi

fence=$(printf '`%.0s' $(seq "$length"))

{
  printf '%stext\n' "$fence"
  cat "$notes"
  printf '%s\n' "$fence"
} > "$body"
