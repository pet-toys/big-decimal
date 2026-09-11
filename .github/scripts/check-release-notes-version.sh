#!/usr/bin/env bash

# Checks the top section of the release notes against the version being built.
#
# PreparePackageReleaseNotesFromFile takes the top section whatever its header
# says and strips the version line on the way out, so a 1.0.0 package can carry
# the 1.0.0-dev.3 notes with nothing downstream able to tell. Measured: packing
# with -p:PackageVersion=0.0.0-ci produced a nuspec whose releaseNotes element
# was the dev.3 section. A nupkg cannot be corrected once pushed, and the GitHub
# release body is rendered from the same text.
#
# Usage: check-release-notes-version.sh <notes-file> [expected-version]
#
# Without the version only the header's shape is checked, which is all a pull
# request can know: it packs 0.0.0-ci and no tag exists yet.

set -euo pipefail

notes=$1
expected=${2-}

# Without the file, PreparePackageReleaseNotesFromFile does not run and the
# placeholder in Directory.Build.targets ships as the release notes - non-empty,
# so the export check upstream of this one passes it.
if [[ ! -f $notes ]]; then
  echo "::error title=Release notes missing::$notes does not exist. The packaging target only runs when it does, so the build would ship the Directory.Build.targets placeholder as the packed <releaseNotes> and as the release body."
  exit 1
fi

# The tag pattern from build-deploy.yml without the v, so a header cannot pass
# here and fail there. beta is absent in both: prerelease identifiers order as
# strings, and beta would sort below dev.
pattern='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(dev|rc|preview)\.(0|[1-9][0-9]*))?$'

# The file is CRLF in a Windows working tree; * text=auto checks it out LF on a
# Linux runner, but the strip keeps this correct wherever it is run.
header=$(sed -n '1p' "$notes" | tr -d '\r')
underline=$(sed -n '2p' "$notes" | tr -d '\r')

if [[ ! $header =~ $pattern ]]; then
  echo "::error title=Malformed release notes header::The first line of $notes is '$header', which is not a bare semantic version. Expected 1.2.3, or a prerelease such as 1.2.3-dev.4 (dev, preview, rc). The export takes the top section whatever the header says, so a header nobody can check is a header nobody does."
  exit 1
fi

# ReleaseNotesHeaderPattern needs an =-underline to find the header it strips.
# Without one the replace is a no-op and the version line ships inside the notes.
# Trailing whitespace and all: ReleaseNotesHeaderPattern matches `=+[ 	]*`, and
# failing a release over a space the target accepts would be this check's own bug.
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
