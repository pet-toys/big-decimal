<!-- Generated from the maintainers' template; local edits are overwritten. -->

# Copilot instructions

A small, focused .NET repository that publishes one or more NuGet packages. Every
package is a project under `src/`, with a matching test project under `test/`.
Pull requests target the default branch.

## Build and test

- The SDK version is pinned in `global.json`; do not bypass it.
- `dotnet restore`, then
  `dotnet test --no-restore --configuration Release --filter Category!=Integration`.
- Tests that need something external either carry `[Trait("Category", "Integration")]`
  and are filtered out of the run above, or self-skip through a `DockerRequired*`
  attribute when no Docker engine is present. Follow whichever the repository
  already uses.

## Conventions

- The packages are multi-targeted; the list of target frameworks lives in
  `Directory.Build.props`. Code must compile on every target in that list - guard
  newer BCL APIs behind a conditional instead of dropping a target.
- Central Package Management. Versions live in `Directory.Packages.props` and
  `test/Directory.Packages.props`. Never put a `Version` attribute on a
  `PackageReference`; add a `PackageVersion` entry and keep the existing
  `[x.y.z,)` range notation.
- Nullable is enabled and implicit usings are disabled - write explicit `using`
  directives.
- Outside Debug, warnings are errors, so anything an analyzer reports fails the
  build.
- Code style and analyzer severities are defined in `.editorconfig` and nowhere
  else - read it instead of guessing, including the nested `.editorconfig` files
  under `test/` and `bench/` that carry the per-folder relaxations. Every severity
  line there is captioned with the rule it enables. Do not add a `.globalconfig`,
  a `GlobalSuppressions.cs` for a rule-wide opt-out, or a `NoWarn` for a
  diagnostic `.editorconfig` can set.
- The public API carries XML documentation.
- Every assembly is strong-named and public-signed; leave the signing properties alone.
- `assets/RELEASE-NOTES.txt` is the source for the packed `<releaseNotes>` and for
  the GitHub release body: publishing a release replaces whatever the release form
  says with the top section of this file. Update it in the pull request that
  prepares a release, newest version on top; never in an unrelated change.
- Package metadata that differs per package (`Description`, `PackageTags`) belongs
  in the project file; only settings shared by every package belong in
  `Directory.Build.props`.
- The README shipped inside a package is the one its `PackageReadmeFile` points at.
  In a multi-package repository each project carries its own; the repository-root
  README is a landing page and is not packed.
- When the repository has solution filters, respect the split: the `*.build.slnf`
  filter is what the release pipeline packs, `*.tests.slnf` is what CI runs.

## Tests

- xunit v3 with AwesomeAssertions; Bogus for test data, Moq for fakes, Testcontainers
  where a real server is required.
- Prefer exercising behaviour through the public API.

## Commits and pull requests

- English only - code, comments, commit messages, pull request descriptions.
- Conventional commit prefixes: `feat`, `fix`, `perf`, `docs`, `test`,
  `build` (dependencies, packaging), `ci` (workflows), `chore`.
- Imperative subject, roughly 72 characters or less.
- No AI attribution and no tool names in commits or pull request descriptions.
