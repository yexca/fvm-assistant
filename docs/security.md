# Sensitive information review policy

The game browser persists login state. `%LOCALAPPDATA%\FvmAssistant\profiles\` contains cookies and Flash/browser storage. The `main` profile keeps its captures, recordings, card images, settings, and logs directly under `%LOCALAPPDATA%\FvmAssistant\`; other profiles keep these under `accounts\<profile>\`. Saved track copies and manually created support tracks may live under `edited-tracks\`. Captures and logs can show account names, room passwords, or other private game data. Keep the entire runtime directory outside the repository and do not share it wholesale.

## What may enter Git

Source code, documentation, small source-owned UI templates, the local Flash test page, and build/check scripts may be staged after review. `vendor/`, `sample/`, `bin/`, and `obj/` must remain local. The Flash DLL and track editor package/data are third-party material and are intentionally ignored. Do not force-add them. Also exclude credentials, private keys, `.env` files, profile data, captures, recordings, and logs.

The `.gitignore` prevents ordinary accidental staging. `scripts/check-secrets.ps1` checks both forbidden paths and high-confidence token formats. `make secrets` scans tracked plus untracked, non-ignored working files. `make check-staged` reads the exact Git index blobs, including staged versions that differ from the working tree. The repository-local `.githooks/pre-commit` runs the staged check when enabled with `make install-hooks` (equivalent to `git config --local core.hooksPath .githooks`). The hook is a local guardrail; it does not install itself on other clones.

## Review before staging or sharing

1. Run `make secrets` and inspect `git status --short --untracked-files=all` before staging. Review any newly added binary or image manually; the text scanner does not inspect its contents.
2. Stage only intended source/doc files. Then inspect `git diff --cached --stat` and `git diff --cached` for URLs with session query strings, cookies, passwords, API keys, identifiers, machine-specific paths, and copied log text. Run `make check-staged`.
3. Review screenshots, recordings, and logs before attaching them to an issue, message, or release. Crop or redact account names, login state, tokens, room passwords, and unrelated personal information. `UrlText.Redact` removes query strings in normal application logging, but do not assume all diagnostics are sanitized.
4. If material was staged by mistake, unstage it and remove the secret from the file. If it reached a commit or another person, rotate/revoke the credential and clean the affected history before publishing. Deleting only the working-tree copy does not remove a committed secret.

The scanner intentionally avoids printing matched values. It cannot recognize every secret, inspect binary content, or determine whether an ordinary-looking string is private. Human review remains required.
