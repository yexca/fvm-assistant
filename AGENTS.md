# Agent instructions

This repository is a Windows-only prototype for **美食大战老鼠**. Read `README.md`, `docs/automation.md`, and `docs/security.md` before changing behavior or handling diagnostics. Keep documentation and newly added prose in English; retain Chinese game labels, file names, card names, and track syntax where they identify actual in-game or third-party data.

## Project boundaries

- The application is `src/FvmAssistant/`: WinForms, .NET Framework 4.8, x64, CefSharp 84.4.10. Build with `scripts/build.ps1` or `make build`, which uses Visual Studio MSBuild. Do not assume `dotnet build` is the supported path.
- `vendor/` and `sample/` are local third-party material. Do not edit, stage, redistribute, or copy their contents into source files. `bin/` and `obj/` are generated output.
- Runtime state is under the ignored `.fvm-data/` directory at the repository root. Profiles hold login cookies; captures, recordings, and logs can identify an account or expose room passwords. Never stage them or paste them into a report without redaction. The legacy `%LOCALAPPDATA%\FvmAssistant\` directory may still exist on older installations.
- Preserve the off-screen capture and CEF input design. Do not introduce game-protocol automation or hard-coded account credentials.

## Work and verification

1. Inspect existing code and the relevant documentation before editing. Keep changes scoped and update docs when behavior or setup changes.
2. For code changes, run `make check` (or `scripts/check-secrets.ps1` followed by `scripts/build.ps1`). For documentation-only changes, run `make secrets` and check links and commands manually.
3. There is no automated gameplay test suite. Use the local test page in `docs/prototype-findings.md` when changing capture/input behavior; live game claims require an explicit manual test.
4. Before any future commit, inspect `git status --short`, `git diff --cached --stat`, and run `make check-staged`. Never force-add ignored files to bypass the policy.
5. Report checks actually run and distinguish build success from gameplay verification.

## Security review

Follow `docs/security.md`. The scanner reports only locations and rule names and has deliberate limits: review screenshots, binary files, logs, URLs, and unusual token formats manually. Treat any suspicious content as private until reviewed. If a secret is exposed, remove it from the proposed change and rotate or revoke it as appropriate before sharing.
