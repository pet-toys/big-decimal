# Security Policy

## Supported versions

Every release of every package in this repository carries the same version and
targets `net8.0`, `net9.0` and `net10.0` together, so the package version line
is independent of any single .NET version. Only the latest release of the
current major line receives security fixes.

| Version | Supported          |
| ------- | :----------------: |
| 1.0.x   | :white_check_mark: |

Prereleases are previews of the release above them, published to GitHub
Packages rather than to nuget.org. Each one is superseded by the next, and none
is supported once the release it previews has shipped.

## Reporting a vulnerability

Please do not report security vulnerabilities through public issues, pull
requests, or discussions.

Instead, use GitHub's private vulnerability reporting: open the repository's
**Security** tab and click **Report a vulnerability**. This keeps the report
confidential until a fix is available.

When reporting, please include as much of the following as you can:

- A description of the vulnerability and its impact.
- The affected package version(s) and target framework.
- Steps to reproduce, ideally with a minimal sample.
- Any known workarounds or mitigations.

## What to expect

- We aim to acknowledge a report within a few days.
- We will keep you informed as we investigate and work on a fix.
- Once a fix ships, we will publish a security advisory and credit the
  reporter, unless you prefer to remain anonymous.

## Scope

These packages are a numeric value type and the code that maps it to and from
PostgreSQL `numeric` and ClickHouse `Decimal*` columns. They do not open
connections, manage credentials, or build SQL text on your behalf: the caller
supplies an already configured connection.

In scope are reports about values being corrupted rather than rejected - a
parsed, formatted, converted, or round-tripped value that silently comes back
different, an overflow that wraps instead of throwing `OverflowException`, or a
database mapping that truncates or misplaces the decimal point. Untrusted input
that the parser turns into unbounded work or memory use is in scope as well.

Out of scope are vulnerabilities in PostgreSQL, ClickHouse, or their .NET
drivers, and issues caused solely by how a consuming application builds its
connection strings, protects its credentials, or chooses the column types it
reads from and writes to.
