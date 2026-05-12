# Codex instructions for Timer

## Main goal

Minimize Codex usage while still producing correct, reviewable code changes.

## Default behavior

- Do not build the project unless I explicitly ask you to.
- Do not run full test suites, full lint, full typecheck, dependency installs, migrations, dev servers, Docker builds, or long-running commands unless I explicitly approve.
- Prefer static inspection, targeted file reads, and minimal diffs.
- Do not explore the whole repository unless necessary. Start from the files directly relevant to the task.
- Do not make speculative refactors or cleanup unrelated to the request.
- Do not add dependencies unless I explicitly approve.
- Do not repeatedly retry failing commands. If a command fails once, stop and summarize the failure.

## Reasoning effort

- Default reasoning effort: Medium.
- Use Low for cosmetic, localized, or clearly simple changes.
- Ask before using High.
- Never use Max or XHigh unless I explicitly request it.
- Do not compensate for lower reasoning by running builds, tests, lint, or typecheck automatically. Ask me to run them manually.

## Safety rules

- Never delete files without separate explicit confirmation in the same message.
- Never run destructive commands such as `rm`, `del`, `rmdir`, `git clean`, `git reset --hard`, or removing directories unless I explicitly approve that exact action.
- If deletion seems necessary, explain why and ask me to do it manually.
- Do not modify generated files, lockfiles, snapshots, or formatting-only files unless explicitly requested.
- Do not change public APIs, data formats, or persisted state schemas unless required by the task.

## Before editing

For non-trivial tasks:

1. Briefly state the files you plan to inspect.
2. State the smallest change you expect to make.
3. Mention whether verification requires a build, test, lint, typecheck, or manual run.

Keep this brief.

## During edits

- Make the smallest safe change.
- Preserve existing style and patterns.
- Prefer localized fixes over broad rewrites.
- Avoid unrelated cleanup.
- Avoid speculative architecture changes.
- Stop and ask before making changes that affect many files.
- If requirements are unclear, make a narrow assumption and state it briefly instead of exploring widely.

## Verification

Do not run expensive verification automatically.

Instead, tell me exactly what to run locally using this format:

```text
Verification needed:
1. <command>
2. What to look for: <expected result>
3. If it fails, paste back: <specific output/logs needed>
```

Assume I can manually run builds, tests, lint, typecheck, app startup, and bug reproduction steps, then paste the output back.

## After editing

Return only:

1. Summary of changed files.
2. What changed.
3. Verification not run, if skipped.
4. Exact manual verification commands for me to run.
5. Any risks or assumptions.

Be concise. Do not include long explanations unless I ask.
