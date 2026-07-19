## MODIFIED Requirements

### Requirement: Containers SHALL NOT auto-restart on failure

All services SHALL use `restart: "no"` policy, ensuring that failures during local development are visible and not silently retried. This requirement applies regardless of whether health checks are configured.

#### Scenario: Failed container stays stopped

- **WHEN** a service fails to start
- **THEN** the container remains in stopped state
- **AND** the exit code and error logs are visible in logs

## ADDED Requirements

### Requirement: Local compose SHALL include healthcheck configuration

The `docker-compose.local.yml` file SHALL define a `healthcheck:` block for the `control-plane` service with the same configuration as the production compose file (`docker-compose.yml`).

#### Scenario: Healthcheck is configured in local compose

- **WHEN** `docker compose -f docker-compose.local.yml up -d` is executed
- **THEN** the control-plane service has a healthcheck with:
  - `test: ["CMD", "curl", "-f", "http://localhost:5000/healthz/ready"]`
  - `interval: 15s`
  - `timeout: 5s`
  - `retries: 2`
  - `start_period: 15s`

### Requirement: Local compose data-plane SHALL depend on control-plane health

The `docker-compose.local.yml` file SHALL define `depends_on` for the `data-plane` service with `condition: service_healthy` targeting the `control-plane` service.

#### Scenario: Data plane waits for control plane readiness in local compose

- **WHEN** `docker compose -f docker-compose.local.yml up -d` is executed
- **THEN** the data-plane container does not start until the control-plane health check returns `Healthy`
