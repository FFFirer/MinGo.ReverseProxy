## ADDED Requirements

### Requirement: ControlPlane SHALL expose liveness health check endpoint

The control plane SHALL expose a `GET /healthz/live` HTTP endpoint that returns `200 OK` when the process is running. This endpoint SHALL NOT perform any dependency checks (database, gRPC, etc.) and SHALL respond within 100ms.

#### Scenario: Liveness check returns healthy

- **WHEN** a client sends `GET /healthz/live` to the control plane
- **THEN** the response status code is `200 OK`
- **AND** the response body contains `"status":"Healthy"`

### Requirement: ControlPlane SHALL expose readiness health check endpoint

The control plane SHALL expose a `GET /healthz/ready` HTTP endpoint that returns `200 OK` only when all readiness checks pass. The readiness check SHALL verify that the `AppDbContext` database is reachable and queryable.

#### Scenario: Readiness check returns healthy when database is accessible

- **WHEN** the control plane database is reachable and queryable
- **AND** a client sends `GET /healthz/ready` to the control plane
- **THEN** the response status code is `200 OK`
- **AND** the response body contains `"status":"Healthy"`

#### Scenario: Readiness check returns unhealthy when database is unreachable

- **WHEN** the control plane database is unreachable
- **AND** a client sends `GET /healthz/ready` to the control plane
- **THEN** the response status code is `503 Service Unavailable`
- **AND** the response body contains `"status":"Unhealthy"`

### Requirement: DataPlane SHALL expose liveness health check endpoint

The data plane SHALL expose a `GET /healthz/live` HTTP endpoint that returns `200 OK` when the process is running. This endpoint SHALL NOT perform any dependency checks.

#### Scenario: Liveness check returns healthy

- **WHEN** a client sends `GET /healthz/live` to the data plane
- **THEN** the response status code is `200 OK`
- **AND** the response body contains `"status":"Healthy"`

### Requirement: ControlPlane Dockerfile SHALL include curl for HEALTHCHECK

The control plane Dockerfile runtime stage SHALL install `curl` via `apt-get` and define a `HEALTHCHECK` directive using `GET /healthz/live`.

#### Scenario: Docker HEALTHCHECK is defined

- **WHEN** the control plane Docker image is built
- **THEN** the `HEALTHCHECK` instruction is present in the runtime stage
- **AND** the health check command uses `curl -fsS http://localhost:5000/healthz/live`
- **AND** the `start_period` is at least 15 seconds

### Requirement: DataPlane Dockerfile SHALL include curl for HEALTHCHECK

The data plane Dockerfile runtime stage SHALL install `curl` via `apt-get` and define a `HEALTHCHECK` directive using `GET /healthz/live`.

#### Scenario: Docker HEALTHCHECK is defined

- **WHEN** the data plane Docker image is built
- **THEN** the `HEALTHCHECK` instruction is present in the runtime stage
- **AND** the health check command uses `curl -fsS http://localhost:8080/healthz/live`

### Requirement: Compose file SHALL define healthcheck for control-plane

The `docker-compose.yml` and `docker-compose.local.yml` files SHALL define a `healthcheck:` block for the `control-plane` service using `GET /healthz/ready`.

#### Scenario: Compose healthcheck is configured

- **WHEN** `docker compose up` is executed
- **THEN** the control-plane service has a healthcheck with:
  - `test: ["CMD", "curl", "-f", "http://localhost:5000/healthz/ready"]`
  - `interval: 15s`
  - `timeout: 5s`
  - `retries: 2`
  - `start_period: 15s`

### Requirement: DataPlane SHALL wait for control-plane health check before starting

The `docker-compose.yml` and `docker-compose.local.yml` files SHALL define `depends_on` for the `data-plane` service with `condition: service_healthy` targeting the `control-plane` service.

#### Scenario: Data plane starts only after control plane is healthy

- **WHEN** `docker compose up` is executed
- **THEN** the data-plane container does not start until the control-plane health check returns `Healthy`
