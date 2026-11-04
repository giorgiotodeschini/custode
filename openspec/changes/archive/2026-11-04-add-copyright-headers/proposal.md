# Proposal

## Why

Commit `04d5922` added rule 5 to `AGENTS.md`: source files under `src/` and `tests/` must start with the project copyright and AGPL-3.0 header. That rule applies to new and modified files, so the 44 source files that already existed before the rule was added still lack the header. Without this change the repository is inconsistent: the rule is documented, but most of the codebase does not follow it. The gap is small and mechanical, so it is cheapest to close now, before further server work multiplies the files that would need it.

## What Changes

- Add the two-line copyright and AGPL-3.0 header, exactly as written in `AGENTS.md` rule 5, as the first lines of the 44 existing source files that lack it: 19 under `src/Custode.Infrastructure/`, 6 under `src/Custode.Server/` (including `Program.cs`), and 19 under `tests/Custode.Server.Tests/`.
- Verify that every non-generated source file under `src/` and `tests/` starts with the header.

Non-changes:

- No code behavior changes. The header is a comment and does not affect compilation or the test results.
- The wording of rule 5 in `AGENTS.md` is not changed.

## Capabilities

### New Capabilities

None. This change does not introduce product behavior.

### Modified Capabilities

None. No requirement in `openspec/specs/` changes. The change declares `skip_specs: true` in `.openspec.yaml`.

## Scope

In scope:

- The 44 existing source files listed in the Why section: every `.cs` file under `src/` and `tests/` that lacks the header and is not generated.

Out of scope:

- EF Core generated files: `src/Custode.Infrastructure/Migrations/*.cs`, `*.Designer.cs`, and `AppDbContextModelSnapshot.cs`. Rule 5 excludes them.
- Files that already have the header.
- Non-`.cs` files such as configuration, JSON, Markdown, and OpenSpec artifacts.
- Changing the wording of rule 5 in `AGENTS.md`.
- Webui and desktop client components, which do not exist in the repository yet.

## Impact

- **Components**: server only (`src/Custode.Infrastructure`, `src/Custode.Server`, `tests/Custode.Server.Tests`). The webui and client are not present, so they are not affected.
- **Code**: 44 files receive two comment lines at the top. No logic changes.
- **Repository history**: adding three lines at the top of a file does not change the attribution of its existing lines. `git blame` attributes only the three new lines to the header commit, so no blame configuration is needed.
- **Verification**: the project's test suite must still pass on SQLite and PostgreSQL 18 through Testcontainers, and `openspec validate add-copyright-headers --strict` must pass.
- **Dependencies**: none.
- **Data**: none.
