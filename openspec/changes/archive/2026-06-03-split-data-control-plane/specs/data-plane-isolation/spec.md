## ADDED Requirements

### Requirement: Data plane runs as an independent process without database access

The data plane SHALL be a standalone ASP.NET Core process that does NOT reference any EF Core packages or DbContext. It SHALL NOT have a database connection string configured. All configuration (routes, clusters, destinations, certificates) SHALL be obtained exclusively from the control plane via gRPC `ConfigReplication` service. On startup, the data plane SHALL establish a gRPC connection to the control plane before accepting proxy traffic.

#### Scenario: Data plane starts without database access
- **WHEN** the data plane process starts
- **THEN** it does not load any EF Core provider or attempt to open a database connection

#### Scenario: Data plane waits for initial config before serving
- **WHEN** the data plane starts but has not yet received the initial config snapshot from the control plane
- **THEN** it returns 503 Service Unavailable for all proxy requests until the first config is applied

### Requirement: Data plane caches certificates from control plane

The data plane `CertificateManager` SHALL load certificates from the `ConfigSnapshot.certificates` field received via gRPC, rather than querying a database directly. Certificates SHALL be cached in memory with the same `ConcurrentDictionary` structure as the current implementation. The certificate cache SHALL be refreshed when a new `ConfigSnapshot` containing certificate data is received.

#### Scenario: Data plane loads certificates from config snapshot
- **WHEN** the data plane receives a `ConfigSnapshot` containing `CertificateInfo` entries
- **THEN** it loads each certificate from the provided `certificate_data` bytes into its in-memory cache

#### Scenario: Data plane reloads certificates on command
- **WHEN** the data plane receives a `CMD_RELOAD_CERTS` control command via heartbeat response
- **THEN** it sends an event requesting certificate update and waits for the updated config snapshot

### Requirement: Data plane reports telemetry to control plane

The data plane SHALL collect request metrics (total requests, error requests, response duration) in its in-memory `TelemetryStore`. These metrics SHALL be periodically sent to the control plane as part of `HeartbeatRequest.metrics`. The data plane SHALL NOT persist metrics locally.

#### Scenario: Data plane sends metrics batch in heartbeat
- **WHEN** the data plane sends a heartbeat
- **THEN** it includes accumulated `MetricPoint` values for request count, error count, and duration in the `HeartbeatRequest`

### Requirement: Data plane has no UI or management endpoints

The data plane SHALL expose only the YARP reverse proxy ports. It SHALL NOT expose any management API, Swagger UI, or Blazor endpoints. Health check endpoint (`GET /health`) is the only non-proxy endpoint allowed.

#### Scenario: Data plane health check returns status
- **WHEN** a request is made to `GET /health` on the data plane
- **THEN** it returns 200 OK if the proxy is running and has valid config, or 503 if not yet initialized
