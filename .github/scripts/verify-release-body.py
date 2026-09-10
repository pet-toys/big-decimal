#!/usr/bin/env python3

"""Fails when the rendered release body does not carry the release notes verbatim.

A release body is rendered as GitHub Flavored Markdown in the repository's
context; the notes it is composed from are plain text. Rather than forbid the
constructs that disagree - one blacklist entry per construct somebody thinks of
- this renders the body through GitHub's own renderer and compares the text it
produces against the notes. Anything Markdown consumes, now or after a change
to what Markdown means, shows up as a difference.

The text comparison alone is not enough, and a mention is why: GitHub renders
`@name` as a link to that account while leaving the visible text exactly as it
was, so a body that names two strangers reads identically to one that does not.
A body composed from plain text renders no links at all, so any anchor in the
rendered HTML is a construct the renderer acted on and is reported beside the
diff.

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
        self.links = []

    def handle_starttag(self, tag, attrs):
        if tag == "a":
            attributes = dict(attrs)
            self.links.append(attributes.get("href") or attributes.get("class") or "<anchor>")

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

    if extractor.links:
        print(
            "::error title=Release body renders links::"
            "The rendered body turned text into links, which a body composed from plain text "
            "never does. A mention keeps its visible text, so the comparison below cannot see it: "
            + ", ".join(extractor.links[:10]),
        )
        return 1

    if notes == rendered:
        print(f"The rendered release body carries all {len(notes)} lines of the notes, and renders no link.")
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
