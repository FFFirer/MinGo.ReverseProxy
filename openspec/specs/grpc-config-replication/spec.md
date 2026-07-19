# gRPC Config Replication

**Purpose**: Control plane pushes configuration (routes, clusters, certificates) to data planes via gRPC bidirectional streaming.
## Requirements
### Requirement: Data plane receives config updates via gRPC bidirectional stream

The control plane SHALL expose a gRPC `ConfigReplication` service with a `ReplicateConfig` bidirectional streaming RPC. The data plane SHALL connect to this RPC at startup with a `ConfigSubscription` message containing its instance ID and current config version. The control plane SHALL respond with a full `ConfigSnapshot` containing all routes, clusters, destinations, and certificates. The control plane SHALL push new `ConfigSnapshot` messages whenever configuration changes (routes/clusters/certificates created, updated, or deleted). The ConfigSnapshot pushed on config changes SHALL include the full certificate data, matching the data sent during initial connection. The data plane SHALL apply received config snapshots to its local `IProxyConfigProvider` and trigger YARP hot reload. The data plane MUST NOT access the database directly for configuration.

**ADDED**: The `RouteConfig.transforms_json` field in the ConfigSnapshot SHALL contain the route's transforms serialized as a JSON array of flat dictionaries. The data plane SHALL read this field and set it on the `YarpRouteConfig.Transforms` property.

**MODIFIED**: The data plane SHALL NOT block startup waiting for the initial config snapshot. The data plane SHALL start serving requests immediately (with empty configuration) and apply config asynchronously when it arrives. The data plane SHALL validate each received ConfigSnapshot before applying it; invalid snapshots SHALL be discarded and the old configuration SHALL be kept.

#### Scenario: Data plane receives full config on connect

- **WHEN** a data plane instance starts and connects to the control plane gRPC `ConfigReplication.ReplicateConfig` RPC
- **THEN** the control plane sends a `ConfigSnapshot` with all current routes, clusters, destinations, certificates, and route transforms

#### Scenario: Config change triggers push to all connected data planes

- **WHEN** a user creates/updates/deletes a route, cluster, or certificate via the control plane REST API
- **THEN** the control plane pushes a new `ConfigSnapshot` containing all routes, clusters, destinations, certificates, and route transforms to all connected data planes via the bidirectional stream

#### Scenario: Data plane applies valid config and triggers YARP reload

- **WHEN** the data plane receives a valid `ConfigSnapshot` from the control plane (passes pre-validation)
- **THEN** it updates its `DataPlaneConfigProvider`, reloads the certificate cache, and signals `IChangeToken` to trigger YARP hot reload
- **AND** if routes in the snapshot have `transforms_json` populated, the data plane SHALL set `YarpRouteConfig.Transforms` accordingly

#### Scenario: Data plane rejects invalid config and keeps old config

- **WHEN** the data plane receives a `ConfigSnapshot` that fails pre-validation (empty RouteId, missing ClusterId reference, invalid Address, etc.)
- **THEN** it SHALL NOT update `DataPlaneConfigProvider`, SHALL log a Warning with validation errors, and SHALL keep the current configuration active

#### Scenario: Data plane reconnects after disconnection

- **WHEN** a data plane's gRPC connection to the control plane is interrupted
- **THEN** it SHALL retry with exponential backoff and resend a `ConfigSubscription` after reconnection

### Requirement: Config snapshot contains checksum for integrity

The `ConfigSnapshot` message SHALL include a `checksum` field computed from the serialized config content. The data plane SHALL compare checksums before applying to avoid processing identical snapshots.

#### Scenario: Data plane skips unchanged config

- **WHEN** the data plane receives a `ConfigSnapshot` whose checksum matches its current config
- **THEN** it SHALL NOT trigger a YARP reload

