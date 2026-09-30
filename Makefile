.DEFAULT_GOAL := help

POWERSHELL ?= powershell.exe
PSFLAGS := -NoProfile -ExecutionPolicy Bypass -File

.PHONY: help build release secrets test-secrets test-friend-pair test-automation check check-staged install-hooks

help:
	@echo Available targets:
	@echo   build          Restore and build Debug with Visual Studio MSBuild
	@echo   release        Restore and build Release
	@echo   secrets        Scan all Git candidates in the working tree
	@echo   test-secrets   Test working-tree and staged secret checks
	@echo   test-friend-pair Test the sparse area menu mapping without game input
	@echo   test-automation Test automation logic with synthetic frames and input
	@echo   check          Run secrets scan, scanner tests, and Debug build
	@echo   check-staged   Scan exactly what is staged for a future commit
	@echo   install-hooks  Enable the repository-local pre-commit hook

build:
	$(POWERSHELL) $(PSFLAGS) scripts/build.ps1 -Configuration Debug

release:
	$(POWERSHELL) $(PSFLAGS) scripts/build.ps1 -Configuration Release

secrets:
	$(POWERSHELL) $(PSFLAGS) scripts/check-secrets.ps1

test-secrets:
	$(POWERSHELL) $(PSFLAGS) scripts/test-check-secrets.ps1

test-friend-pair:
	$(POWERSHELL) $(PSFLAGS) scripts/test-friend-pair.ps1

test-automation: build
	$(POWERSHELL) $(PSFLAGS) scripts/test-automation.ps1

check: secrets test-secrets test-friend-pair test-automation

check-staged:
	$(POWERSHELL) $(PSFLAGS) scripts/check-secrets.ps1 -Staged

install-hooks:
	git config --local core.hooksPath .githooks
