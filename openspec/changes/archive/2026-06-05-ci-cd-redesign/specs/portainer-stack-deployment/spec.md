## ADDED Requirements

### Requirement: Portainer-compatible docker-compose

The docker-compose.yml SHALL be compatible with Portainer Stack import, using pre-built registry images.

#### Scenario: Image-based services
- **WHEN** `docker compose up` or Portainer loads the stack
- **THEN** the `control-plane` service SHALL use image `registry.private.fffirer.top:9081/1mingo/reverseproxy-cp:latest`
- **AND** the `data-plane` service SHALL use image `registry.private.fffirer.top:9081/1mingo/reverseproxy-dp:latest`
- **AND** the `frontend` service SHALL use image `registry.private.fffirer.top:9081/1mingo/reverseproxy-fe:latest`

#### Scenario: Local build fallback
- **WHEN** a developer runs `docker compose build`
- **THEN** the compose SHALL support local building using the original Dockerfiles
- **AND** the `build` context SHALL be preserved for each service

### Requirement: Database migration service

The compose SHALL include a one-shot `migrate` service for EF Core migrations.

#### Scenario: Migration execution
- **WHEN** `docker compose up` is run
- **THEN** the `migrate` service SHALL use the cp image
- **AND** SHALL run `./efbundle --connection "Data Source=/app/data/mingocp.db"` as entrypoint
- **AND** SHALL use a named volume or bind mount for the SQLite data directory
- **AND** SHALL have `restart: "no"`
- **AND** the `control-plane` service SHALL depend on `migrate` with `condition: service_completed_successfully`

### Requirement: Port mapping and environment

The compose SHALL expose correct ports and environment variables for each service.

#### Scenario: ControlPlane ports
- **WHEN** the `control-plane` service runs
- **THEN** port `5000` SHALL be mapped for HTTP (REST API)
- **AND** port `5001` SHALL be mapped for gRPC
- **AND** `ASPNETCORE_URLS`, `ASPNETCORE_ENVIRONMENT`, and `ConnectionStrings__DefaultConnection` SHALL be set

#### Scenario: DataPlane configuration
- **WHEN** the `data-plane` service runs
- **THEN** port `8080` SHALL be mapped
- **AND** `ControlPlane__GrpcUrl=http://control-plane:5001` SHALL be set

#### Scenario: Frontend configuration
- **WHEN** the `frontend` service runs
- **THEN** port `8081:80` SHALL be mapped

### Requirement: Data persistence

The compose SHALL persist SQLite data across container restarts.

#### Scenario: Data volume mount
- **WHEN** the `control-plane` and `migrate` services run
- **THEN** the SQLite database SHALL be stored in a persistent volume or bind mount at `/app/data`
- **AND** both services SHALL share the same data volume
