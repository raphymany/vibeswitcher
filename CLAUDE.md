# VibeSwitcher — Claude Working Instructions

These rules apply to every session and every task. Read this file at the start of each conversation before doing anything else.

---

## 1. Session Start Checklist

Before writing a single line of code or making any plan, always do these four things:

1. **Read `AUDIT2.md`** — this is the live list of every open/uncompleted task. It tells you what is still left to do and which branches are planned or in progress.
2. **Read `AUDIT.md`** — this is the full audit with all details, section numbers, and the branch execution log (Section 12). Use it for context and to understand the scope of any item.
3. **Check GitHub** — run `gh pr list` (open and recently merged) and `gh issue list` to see the current state of PRs and issues. Never assume the repo state from memory alone.
4. **Check `git log --oneline -10`** — confirm what is on `main` and what the last merged commit was.

Only after completing these four steps should you form a plan or begin any work.

---

## 2. Work Process: Plan → Execute → QA → Commit

Every non-trivial implementation task must follow this exact order. Never skip steps.

### Step 1 — Plan
- Write out what you intend to do before touching any files.
- For anything more than a one-line fix, confirm the plan with the user first.
- Reference the relevant AUDIT.md section number and branch name.

### Step 2 — Execute
- Implement all changes.
- Keep changes focused on the branch scope — do not add unrelated cleanup or features.

### Step 3 — QA Review (mandatory)
- After implementation is complete, spawn a background `general-purpose` or `Explore` subagent to review all changed files.
- The QA agent should check for: bugs, logic errors, edge cases, security issues, missing test coverage, and anything that could be done better.
- Wait for the QA agent to complete. Address every finding before moving to the next step.
- Never commit before QA has reviewed.

### Step 4 — Commit and PR
- Only commit after QA is clean.
- Follow the PR rules in Section 4 below.
- After the PR is merged, follow the post-merge update rules in Section 5 below.

---

## 3. Response Style

- **Never show full code blocks or diffs** in responses. Describe what changed in plain English (e.g., "Updated `ConfigService.Save` to write a backup before overwriting the main file").
- Only show code when the logic is subtle enough that a description would mislead — and flag it explicitly when doing so ("showing this because the logic is non-obvious").
- After reading a file, do not narrate its contents — just summarize what is relevant.
- Keep end-of-turn summaries short: one or two sentences covering what changed and what is next. Nothing else.
- The user does not have deep C# expertise — explain technical decisions in plain language when relevant.

---

## 4. PR Rules

### Format
- PR descriptions must follow the established pattern from PRs #33, #35, #36:
  - A short prose intro explaining what the branch does and why.
  - `## What changed` — one bullet per meaningful change.
  - `## Notable test coverage` — if tests were added.
  - `## How to verify` — how to manually confirm the change works.
  - `## Test results` — test count and run time.
- Use plain `- ` bullets everywhere. **Never use `- [ ]` checkbox / task list syntax** anywhere in a PR description.
- **Never include internal audit codes** (TD3, R2, 7.5, etc.) in PR titles or descriptions — these mean nothing to someone reading the PR. Describe what was resolved in plain English instead.
- **Never include "Branch [X]" or branch names** in PR titles or descriptions — the branch name is already visible in GitHub and adds no value to the PR.

### Tooling
- Always use a **single-quoted here-string** (`@'...'@`) for PR body content — backticks are literal inside single-quoted strings; double-quoted strings (`@"..."@`) interpret backticks as escape characters and corrupt inline code formatting.
- For `gh pr create`, write the body to a temp file with `Out-File -Encoding utf8` and use `--body-file`:
  ```powershell
  $body = @'
  ...body content with `backticks` safe...
  '@
  $body | Out-File -Encoding utf8 "$env:TEMP\pr-body.md"
  gh pr create --title "..." --body-file "$env:TEMP\pr-body.md"
  ```
- For editing an existing PR (`gh pr edit` is unreliable), use `Invoke-RestMethod` directly against the GitHub API:
  ```powershell
  $token = gh auth token
  $payload = [ordered]@{ title = $prTitle; body = $prBody } | ConvertTo-Json -Compress -Depth 3
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
  Invoke-RestMethod -Method Patch -Uri "https://api.github.com/repos/raphymany/vibeswitcher/pulls/N" `
      -Headers @{ Authorization = "Bearer $token"; Accept = "application/vnd.github+json" } `
      -Body $bytes -ContentType "application/json; charset=utf-8"
  ```

### Issue linking
- Always include `Closes #N` in the PR body for the GitHub issue the branch addresses.
- If no issue exists yet, create one with `gh issue create` before opening the PR, then reference it.
- Before `gh pr create`, run `gh issue list` to find the right issue number.

---

## 5. Post-Merge Updates (mandatory after every merged PR)

After every branch is merged, update these three files before starting any new work. Do not defer this.

### `AUDIT.md`
- Mark completed items as `✅ Done — PR #N`.
- Add the branch as a numbered section under Section 12 with a brief summary.
- Update the Recommended Execution Order table and summary counts.

### `AUDIT2.md`
- Remove completed items from all open-item sections.
- Mark the branch row in Section 12 as `✅ Merged — PR #N`.
- Update the "Last updated" header date.

### `CHANGELOG.md`
- Add or update the `[Unreleased]` section at the top with `### Added` and `### Fixed` entries for everything in the merged branch.
- When a release is cut, rename `[Unreleased]` to the version number and date.

---

## 6. UI and Dialog Rules

- **Never use `MessageBox.Show` or any native OS dialog.** Always create a custom styled dialog window that matches the app's design (white card, `#F3F3F3` background, rounded buttons, icon). The existing `AlertDialog` and `ConflictRetryDialog` in `Views/` are the patterns to follow.
- Every new dialog must: use `WindowStartupLocation="CenterOwner"`, set `Owner` before `ShowDialog()`, use `ShowInTaskbar="False"`, and use the global `ActionButton` / `PrimaryButton` / `DangerButton` styles from `App.xaml`.

---

## 7. Project Context

- **What it is:** VibeSwitcher — a Windows system tray app for switching audio devices via profiles and global hotkeys. Built with WPF, .NET 8, C#.
- **Config file:** `%APPDATA%\VibeSwitcher\config.json` (atomic write, backup on every save).
- **Test project:** `VibeSwitcher.Tests` — xUnit, runs in ~2 seconds with `dotnet test`.
- **Key architecture files:** `App.xaml.cs` (orchestration), `Services/`, `ViewModels/`, `Views/`.
- **AUDIT.md** = full audit, all sections, branch log. **AUDIT2.md** = only open/incomplete items. Both must stay in sync.
