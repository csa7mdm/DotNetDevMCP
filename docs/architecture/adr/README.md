# Architecture Decision Records (ADR)

This directory contains Architecture Decision Records for DotNetDevMCP.

## What is an ADR?

An Architecture Decision Record (ADR) captures an important architectural decision made along with its context and consequences.

## Format

Each ADR follows this structure:
- **Title**: Short descriptive title
- **Status**: Proposed | Accepted | Deprecated | Superseded
- **Date**: When the decision was made
- **Context**: What is the issue we're seeing that is motivating this decision?
- **Decision**: What is the change we're proposing and/or doing?
- **Consequences**: What becomes easier or more difficult to do because of this change?

## Index

| ID | Title | Status | Date |
|----|-------|--------|------|
| [ADR-001](001-fork-sharptools.md) | Fork SharpTools instead of pluggable integration | Accepted | 2025-12-30 |
| [ADR-002](002-concurrent-operations-priority.md) | Prioritize concurrent operations | Accepted | 2025-12-30 |
| [ADR-003](003-deep-git-integration.md) | Implement deep Git integration | Accepted | 2025-12-30 |
| [ADR-004](004-test-driven-development.md) | Adopt Test-Driven Development | Accepted | 2025-12-30 |
| [ADR-005](005-ai-friendly-documentation.md) | AI-friendly documentation as first-class citizen | Accepted | 2025-12-30 |

## Creating a New ADR

1. Copy `template.md` to a new file with format `NNN-short-title.md`
2. Fill in the sections
3. Submit for review
4. Update this index

## References

- [ADR GitHub Organization](https://adr.github.io/)
- [Documenting Architecture Decisions by Michael Nygard](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
