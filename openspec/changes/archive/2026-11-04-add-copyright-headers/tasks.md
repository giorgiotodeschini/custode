# Tasks

The governing requirement for every task is rule 5 of `AGENTS.md` (copyright header on source files under `src/` and `tests/`). This change declares `skip_specs: true`, so there are no spec requirements to map to. Scope is the server component only; the webui and desktop client are not present.

## 1. Apply the copyright header

- [x] 1.1 Prepend the two header lines from `AGENTS.md` rule 5, followed by one blank line, to each of the 44 existing non-generated `.cs` files under `src/` and `tests/` that do not start with it. The list is built with `git ls-files -- src tests`, filtered to exclude `Migrations/`, `*.Designer.cs`, `obj/`, and `bin/`, and then checked against the header. Verify: the check from task 2.1 finds no file without the header, and `git diff --numstat` shows exactly 3 added lines (2 header lines and a blank line) and 0 deleted lines for each of the 44 files.
- [x] 1.2 Verify that no generated file changed. Verify: `git diff --name-only` contains no path under `src/Custode.Infrastructure/Migrations/`.

## 2. Verification

- [x] 2.1 Run the header check over every non-generated `.cs` file under `src/` and `tests/`. Verify: zero files are reported without the header.
- [x] 2.2 Run the full test suite with `dotnet test` on SQLite and on PostgreSQL 18 through Testcontainers. Verify: zero failures, and the test count equals the count before this change.
- [x] 2.3 Run `openspec validate add-copyright-headers --strict`. Verify: zero errors and zero warnings.

## Workflow follow-up

- Commit the header change as a single commit.
- Archive the change after the project's review requirements are satisfied.
