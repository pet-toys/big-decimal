#!/usr/bin/env bash

# Checks the top section of the release notes against the version being built:
# PreparePackageReleaseNotesFromFile takes the top section whatever its header
# says, so a 1.0.0 package could carry the 1.0.0-dev.3 notes with nothing
# downstream able to tell.
#
# Usage: check-release-notes-version.sh <notes-file> [expected-version]
#
# Without the version only the header's shape is checked, which is all a pull
# request can know.

set -euo pipefail

notes=$1
expected=${2-}

# Without the file the Directory.Build.targets placeholder ships as the notes,
# and it is non-empty, so the export check passes it.
if [[ ! -f $notes ]]; then
  echo "::error title=Release notes missing::$notes does not exist. The packaging target only runs when it does, so the build would ship the Directory.Build.targets placeholder as the packed <releaseNotes> and as the release body."
  exit 1
fi

# The tag pattern from build-deploy.yml without the v; change both together.
pattern='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(dev|rc|preview)\.(0|[1-9][0-9]*))?$'

# The file is CRLF in a Windows working tree.
header=$(sed -n '1p' "$notes" | tr -d '\r')
underline=$(sed -n '2p' "$notes" | tr -d '\r')

if [[ ! $header =~ $pattern ]]; then
  echo "::error title=Malformed release notes header::The first line of $notes is '$header', which is not a bare semantic version. Expected 1.2.3, or a prerelease such as 1.2.3-dev.4 (dev, preview, rc). The export takes the top section whatever the header says, so a header nobody can check is a header nobody does."
  exit 1
fi

# Without the =-underline ReleaseNotesHeaderPattern strips nothing and the
# version line ships inside the notes. Trailing blanks allowed, as the target allows them.
if [[ ! $underline =~ ^=+[[:blank:]]*$ ]]; then
  echo "::error title=Missing release notes underline::Line 2 of $notes is '$underline'. The header is stripped by matching the version line and an =-underline beneath it, so without the underline the version ships as the first line of the packed <releaseNotes> and of the release body."
  exit 1
fi

if [[ -n $expected && $header != "$expected" ]]; then
  echo "::error title=Release notes are for another version::$notes opens with '$header' but the build is $expected. The top section is what ships, so this release would carry the notes of $header. Add the $expected section above the current top one, or correct the header."
  exit 1
fi

if [[ -n $expected ]]; then
  echo "Release notes open with $header, which is the version being built."
else
  echo "Release notes open with $header; no tag to compare it against."
fi
