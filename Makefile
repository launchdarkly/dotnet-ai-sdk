# Developer and CI entry points. CI runs the same targets, so a green `make` run
# locally means the same checks pass in CI (apart from the Windows and macOS legs).

SOLUTION := LaunchDarkly.AI.Server.sln
CONFIGURATION ?= Release
DOTNET ?= dotnet
# Paths of the public API files that api-check keeps in sync with the code.
PUBLIC_API_FILES := $(wildcard src/*/PublicAPI.*.txt)

.PHONY: build test lint format format-check api-check api-compat docs pack pack-check clean restore tools

restore:
	$(DOTNET) restore $(SOLUTION)

tools:
	$(DOTNET) tool restore

# Release build of every project. Warnings are errors, including an unrecorded
# public API change. Without LaunchDarkly.snk the libraries are public-signed.
build: restore
	$(DOTNET) build $(SOLUTION) --no-restore -c $(CONFIGURATION) -warnaserror

# Unit tests run against the Debug build (not strong-named), as in CI.
# TESTFRAMEWORK=net8.0 limits the run to one target framework.
test: restore
	$(DOTNET) test $(SOLUTION) --no-restore -c Debug --blame-hang-timeout 60s

# Analyzer diagnostics (code quality and public API rules), without changing files.
lint: restore
	$(DOTNET) format analyzers $(SOLUTION) --no-restore --verify-no-changes --severity warn

# Apply whitespace and code-style fixes.
format: restore
	$(DOTNET) format whitespace $(SOLUTION) --no-restore
	$(DOTNET) format style $(SOLUTION) --no-restore --severity warn

# Fail if `make format` would change anything.
format-check: restore
	$(DOTNET) format whitespace $(SOLUTION) --no-restore --verify-no-changes
	$(DOTNET) format style $(SOLUTION) --no-restore --verify-no-changes --severity warn

# Regenerate the PublicAPI.*.txt files from the code and fail if they drift from
# what is committed. A new public member belongs in PublicAPI.Unshipped.txt.
api-check: restore
	$(DOTNET) format analyzers $(SOLUTION) --no-restore --diagnostics RS0016 RS0017 --severity hidden
	@git diff --exit-code -- $(PUBLIC_API_FILES) || { \
		echo "Public API files are out of date. Review the diff above and commit it if the change is intended."; \
		exit 1; }

# Breaking-change check of the public API against a git ref (default: main), with
# ApiCompat (pinned in .config/dotnet-tools.json). The ref is built in a temporary
# worktree; the working tree is the new side. API_COMPATIBLE=true accepts an intended
# break, as the api-compatible pull-request label does in CI.
BASE ?= main
api-compat: tools
	scripts/api-compat.sh $(BASE)

# API reference docs with docfx (pinned in .config/dotnet-tools.json). Output goes to docs/.
docs: tools
	$(DOTNET) tool run docfx docfx.json

pack: build
	$(DOTNET) pack $(SOLUTION) --no-build -c $(CONFIGURATION) -o nupkgs

# Packaging check: pack into a local feed, install each package into a fresh console
# project from that feed plus nuget.org, build and run it. Fails on a missing or
# undeclared dependency, and checks the metapackage carries no assembly.
pack-check:
	scripts/pack-check.sh

clean:
	$(DOTNET) clean $(SOLUTION)
	rm -rf nupkgs api docs
