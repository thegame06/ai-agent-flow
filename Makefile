SHELL := /usr/bin/env bash

ROOT := $(shell pwd)
TEST_SCRIPT := $(ROOT)/scripts/test/ephemeral.sh

.PHONY: help test-env-up test-env-down test-ephemeral test-unit test-integration test-backend test-frontend test-all quality-no-mock

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

test-all: test-ephemeral
