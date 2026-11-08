DECISION: Missing docs/index.md

Date: 2026-02-14
Status: Proposed

Context:
- The README references a documentation index (docs/index.md) and we want a clear entry point for repository documentation.
- At the time of this change there is no docs/index.md in the repository.

Decision:
- Create a documentation index at docs/index.md that links to existing materials under docs/ (architecture, permission_mapping, README.docs-proposal.md, etc.).
- In the short term create a placeholder index.md that can be expanded by documentation maintainers.

Consequences:
- README can link to docs/index.md as a stable entrypoint.
- Documentation maintainers should create or update docs/index.md with structured navigation.

Action items:
- Add a placeholder docs/index.md (follow-up).
- Update README to link to docs/index.md (done in this commit).
- Assign an owner to maintain docs/index.md and expand the index.
