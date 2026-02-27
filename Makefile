SHELL := /usr/bin/env bash

ROOT := $(shell pwd)
TEST_SCRIPT := $(ROOT)/scripts/test/ephemeral.sh

.PHONY: help test-env-up test-env-down test-ephemeral test-unit test-integration test-backend test-frontend test-all quality-no-mock qa-one-shot up-local-full down-local-full clean-local-full restart-local-full refresh-local-full check-qr

help:
	@echo "Available targets:"
	@echo "  make test-env-up        # Start ephemeral Docker infra (mongo/redis/mcp)"
	@echo "  make test-env-down      # Stop/remove ephemeral Docker infra"
	@echo "  make test-ephemeral     # Full cycle: up -> backend tests -> frontend checks -> down"
	@echo "  make test-unit          # Backend unit tests only"
	@echo "  make test-integration   # Backend integration tests only"
	@echo "  make test-backend       # Unit + integration backend tests"
	@echo "  make test-frontend      # Frontend lint/build/test (if test script exists)"
	@echo "  make test-all           # Alias of test-ephemeral"
	@echo "  make quality-no-mock    # Fail if runtime code contains mock/stub/simulated paths"
	@echo "  make qa-one-shot        # Full QA gate (guardrail + backend + frontend)"
	@echo "  make up-local-full      # Start full local stack (infra + api + frontend + qr bridge)"
	@echo "  make down-local-full    # Stop full local stack (keeps docker volumes/data)"
	@echo "  make clean-local-full   # Kill stale stack/processes (keeps docker volumes/data)"
	@echo "  make restart-local-full # Restart stack without wiping data"
	@echo "  make refresh-local-full # Full refresh: clean + wipe volumes + start"
	@echo "  make check-qr CHANNEL_ID=<id> # Debug QR bridge/session for a channel"

test-env-up:
	@bash $(TEST_SCRIPT) up

test-env-down:
	@bash $(TEST_SCRIPT) down

test-ephemeral:
	@bash $(TEST_SCRIPT) run

test-unit:
	@dotnet test tests/AgentFlow.Tests.Unit/AgentFlow.Tests.Unit.csproj -v minimal

test-integration:
	@dotnet test tests/AgentFlow.Tests.Integration/AgentFlow.Tests.Integration.csproj -v minimal

test-backend: test-unit test-integration

test-frontend:
	@for app in frontend/aiagent_flow frontend/designer; do \
		if [[ -f $$app/package.json ]]; then \
			echo "==> $$app"; \
			(cd $$app && npm ci); \
			(cd $$app && npm run lint); \
			(cd $$app && npm run build); \
			if (cd $$app && npm run | grep -q " test"); then (cd $$app && npm test -- --watch=false); else echo "No test script in $$app"; fi; \
		fi; \
	done

quality-no-mock:
	@bash scripts/quality/no-mock-runtime.sh

qa-one-shot:
	@bash scripts/quality/qa-one-shot.sh

up-local-full:
	@bash scripts/local-full-up.sh

down-local-full:
	@bash scripts/local-full-down.sh

clean-local-full:
	@WIPE_DATA=0 bash scripts/local-full-clean.sh

restart-local-full:
	@dotnet build src/AgentFlow.Api/AgentFlow.Api.csproj -v minimal
	@WIPE_DATA=0 bash scripts/local-full-clean.sh
	@bash scripts/local-full-up.sh

refresh-local-full:
	@WIPE_DATA=1 bash scripts/local-full-clean.sh
	@bash scripts/local-full-up.sh

check-qr:
	@if [[ -z "$$CHANNEL_ID" ]]; then echo "Usage: make check-qr CHANNEL_ID=<id>"; exit 1; fi
	@bash scripts/check-qr.sh "$$CHANNEL_ID"

test-all: test-ephemeral
