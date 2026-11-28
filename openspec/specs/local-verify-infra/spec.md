# local-verify-infra Specification

## Purpose

本地自包含验证栈：一条 compose 命令拉起控制面/数据面/前端，数据卷隔离，前端按 Production 托管，日志级别可覆盖，并包含容器健康检查与启动依赖编排。

## Requirements

### Requirement: Local verification infrastructure SHALL provide self-contained stack

The system SHALL provide a `docker-compose.local.yml` file that starts the complete MinGo stack (ControlPlane + DataPlane + frontend) using locally built images, without dependency on external container registries.

#### Scenario: One-command full stack startup
- **WHEN** developer runs `docker compose -f docker-compose.local.yml up -d`
- **THEN** all services build locally and start successfully
- **AND** ControlPlane frontend is accessible at http://localhost:5000
- **AND** DataPlane HTTP proxy is accessible at http://localhost:8080
- **AND** DataPlane HTTPS proxy is accessible at https://localhost:8443
- **AND** database migration runs before ControlPlane starts

### Requirement: Data lifecycle SHALL be fully managed by Docker

The database file SHALL be stored in a Docker named volume, not bind-mounted from the host filesystem. Resetting the infrastructure SHALL completely erase all database state.

#### Scenario: Fresh database on volume reset
- **WHEN** developer runs `docker compose -f docker-compose.local.yml down -v` followed by `docker compose -f docker-compose.local.yml up -d`
- **THEN** a new empty database is created and migrations are applied

#### Scenario: Data isolation from host
- **WHEN** containers are running with `docker compose -f docker-compose.local.yml up -d`
- **THEN** no database files exist in the project directory (no `./data/` or `*.db` files written to host)

### Requirement: Production environment SHALL be used for frontend compatibility

The ControlPlane SHALL run with `ASPNETCORE_ENVIRONMENT=Production` to ensure wwwroot static files are served and SPA fallback routing works.

#### Scenario: Frontend UI is served
- **WHEN** developer opens http://localhost:5000 in a browser
- **THEN** the MinGo management console UI loads correctly
- **AND** SPA client-side routing works (page refreshes on sub-routes resolve correctly)

### Requirement: Log level SHALL be overridable without modifying config files

The default log level SHALL be set to Debug via environment variable overrides, without modifying `appsettings.json` or `appsettings.Production.json`.

#### Scenario: Debug logging is active
- **WHEN** developer views container logs with `docker compose -f docker-compose.local.yml logs -f`
- **THEN** log output includes Debug-level messages

### Requirement: HTTPS proxy port SHALL be exposed for local verification

The DataPlane SHALL expose port 8443 for HTTPS proxy traffic. TLS certificates SHALL be managed by the application's certificate service, not by docker-compose configuration.

#### Scenario: HTTPS port is open
- **WHEN** developer checks exposed ports with `docker compose -f docker-compose.local.yml ps`
- **THEN** port 8443 is listed as exposed for the data-plane service

### Requirement: Containers SHALL NOT auto-restart on failure

All services SHALL use `restart: "no"` policy, ensuring that failures during local development are visible and not silently retried. This requirement applies regardless of whether health checks are configured.

#### Scenario: Failed container stays stopped
- **WHEN** a service fails to start
- **THEN** the container remains in stopped state
- **AND** the exit code and error logs are visible in logs

### Requirement: Local compose SHALL include healthcheck configuration

The `docker-compose.local.yml` file SHALL define a `healthcheck:` block for the `control-plane` service with the same configuration as the production compose file (`docker-compose.yml`).

#### Scenario: Healthcheck is configured in local compose

- **WHEN** `docker compose -f docker-compose.local.yml up -d` is executed
- **THEN** the control-plane service has the same healthcheck as the production compose file

### Requirement: Local compose data-plane SHALL depend on control-plane health

The `docker-compose.local.yml` file SHALL define `depends_on` for the `data-plane` service with `condition: service_healthy` targeting the `control-plane` service.

#### Scenario: Data plane waits for control plane readiness in local compose

- **WHEN** `docker compose -f docker-compose.local.yml up -d` is executed
- **THEN** the data-plane container does not start until the control-plane health check returns `Healthy`
