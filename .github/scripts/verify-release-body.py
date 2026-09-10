#!/usr/bin/env python3

"""Fails when the rendered release body does not carry the release notes verbatim.

A release body is rendered as GitHub Flavored Markdown in the repository's
context; the notes it is composed from are plain text. Rather than forbid the
constructs that disagree - one blacklist entry per construct somebody thinks of
- this renders the body through GitHub's own renderer and compares the text it
produces against the notes. Anything Markdown consumes, now or after a change
to what Markdown means, shows up as a difference.

Usage: verify-release-body.py <notes file> <rendered html file>
"""

import difflib
import sys
from html.parser import HTMLParser


class TextExtractor(HTMLParser):
    """Collects the text a reader sees, with the markup taken back out."""

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.parts = []

    def handle_data(self, data):
        self.parts.append(data)

    def text(self):
        return "".join(self.parts)


def normalize(text):
    """Trailing space and the blank edges are the renderer's, not the author's."""
    lines = [line.rstrip() for line in text.replace("\r\n", "\n").split("\n")]
    while lines and not lines[0]:
        lines.pop(0)
    while lines and not lines[-1]:
        lines.pop()
    return lines


def main(argv):
    if len(argv) != 3:
        print(__doc__.strip(), file=sys.stderr)
        return 2

    with open(argv[1], encoding="utf-8") as handle:
        notes = normalize(handle.read())
    with open(argv[2], encoding="utf-8") as handle:
        extractor = TextExtractor()
        extractor.feed(handle.read())
        rendered = normalize(extractor.text())

    if notes == rendered:
        print(f"The rendered release body carries all {len(notes)} lines of the notes.")
        return 0

    diff = difflib.unified_diff(notes, rendered, "release notes", "rendered body", lineterm="", n=1)
    print(
        "::error title=Release body does not match the release notes::"
        "The rendered body dropped or altered text from assets/RELEASE-NOTES.txt. "
        "A release body is Markdown and the notes are plain text; see the diff in the log.",
    )
    for line in diff:
        print(line)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
