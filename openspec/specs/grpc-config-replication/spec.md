# gRPC Config Replication

## Purpose

Control plane pushes configuration (routes, clusters, certificates) to data planes via gRPC bidirectional streaming, filtered per connection by the data plane's bound node.

## Requirements
### Requirement: Data plane receives config updates via gRPC bidirectional stream

The control plane SHALL expose a gRPC `ConfigReplication` service with a `ReplicateConfig` bidirectional streaming RPC. The data plane SHALL connect to this RPC at startup with a `ConfigSubscription` message containing its instance ID and current config version. The data plane MUST NOT access the database directly for configuration.

The control plane SHALL resolve the connected data plane's bound node and SHALL assemble the `ConfigSnapshot` per connection:

- routes bound to a Cloudflare tunnel SHALL be included for every data plane that is bound to a node (tunnel ingress backhauls to data planes);
- routes bound to `node:<N>` SHALL be included only when `N` equals the data plane's bound node;
- a data plane without a bound node SHALL receive **no config at all** — the control plane SHALL NOT send a `ConfigSnapshot` on registration, and SHALL skip it during broadcasts;
- when a data plane is unbound, the control plane SHALL push an empty snapshot (no routes, no certificates) to clear previously installed rules;
- certificates SHALL be filtered to domains referenced by the included routes (same wildcard matching rule as the data plane SNI selector); an empty snapshot carries no certificates.

Clusters SHALL be included unchanged.

`RouteConfig.transforms_json` in the ConfigSnapshot SHALL carry the route's transforms as a JSON array of flat dictionaries; the data plane SHALL set them on `YarpRouteConfig.Transforms`.

The data plane SHALL NOT block startup waiting for the initial config snapshot: it SHALL serve requests immediately with empty configuration and apply config asynchronously. Each received snapshot SHALL be validated before applying; invalid snapshots SHALL be discarded and the old configuration kept.

Snapshot routes SHALL be produced by a shared rule compiler (`DataPlaneRuleCompiler`) for both the initial snapshot and broadcasts. It SHALL emit only enabled routes, order them deterministically by normalized match key then route ID, and emit at most one rule per match key (lowest route ID). Tunnel-bound enabled routes SHALL be included. Suppressed routes SHALL be logged as warnings.

#### Scenario: Data plane receives filtered config on connect

- **WHEN** a data plane bound to node `华东` connects to `ConfigReplication.ReplicateConfig`
- **THEN** the control plane sends a `ConfigSnapshot` containing all tunnel-bound routes plus `node:华东` routes, and certificates for the referenced domains only

#### Scenario: Unbound data plane receives no config on connect

- **WHEN** a data plane without a bound node connects
- **THEN** the control plane SHALL NOT send any `ConfigSnapshot` on registration

#### Scenario: Binding change pushes a new snapshot

- **WHEN** an instance is bound to a node via `PUT /api/instances/{id}/node`
- **THEN** the control plane SHALL push a snapshot assembled for that node if the instance is connected

#### Scenario: Unbinding pushes an empty snapshot

- **WHEN** an instance is unbound via `PUT /api/instances/{id}/node` with `null`
- **THEN** the control plane SHALL push an empty snapshot (no routes, no certificates) to the connected instance

#### Scenario: Config change pushes per-connection snapshots

- **WHEN** a user creates/updates/deletes a route, cluster, or certificate via the control plane REST API
- **THEN** the control plane SHALL assemble and push a `ConfigSnapshot` per connected **bound** data plane, filtered by that plane's node, via the bidirectional stream

#### Scenario: Node-bound route change reaches only the matching node

- **WHEN** a route bound to `node:华东` is created or updated
- **THEN** data planes bound to node `华东` SHALL receive a snapshot including the route, and data planes of other nodes SHALL receive a snapshot without it

#### Scenario: Certificate snapshot respects node isolation

- **WHEN** the control plane pushes a snapshot to a data plane bound to node `华东`
- **THEN** the snapshot's certificates SHALL include only certificates whose domain matches a host referenced by the routes in that snapshot

#### Scenario: Tunnel-bound enabled route is compiled into the snapshot

- **WHEN** an enabled route's `ConnectionId` refers to an external Cloudflare tunnel
- **THEN** the `ConfigSnapshot` routes include that route with its own match host/path and `ClusterId`
- **AND** the data plane serves the host/path by forwarding to the cluster (the tunnel's next hop is the data plane, not the backend)

#### Scenario: Disabled routes are not compiled into the snapshot

- **WHEN** a route has `Enabled = false`
- **THEN** the compiled snapshot routes exclude it
- **AND** the data plane does not create a YARP rule for it

#### Scenario: Duplicate match keys collapse deterministically with a warning

- **WHEN** two enabled routes in the database share the same normalized host and path
- **THEN** the snapshot carries exactly one rule for that match key: the one with the lowest route ID in lexicographic order
- **AND** regardless of the order routes are read from the database
- **AND** the control plane logs a warning naming the suppressed route

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

### Requirement: Subscription carries agent self-description

The `ConfigSubscription` SHALL carry agent self-description fields `name`, `version`, and `listener_addresses` (proto fields appended with new numbers; older data planes leave them empty). The control plane SHALL use these values when registering the instance and SHALL derive the instance IP from the gRPC connection peer. Empty `name` SHALL fall back to `DataPlane-{id prefix}`; empty `version` SHALL fall back to `unknown`.

#### Scenario: Instance registered with subscription self-description

- **WHEN** a data plane subscribes with `name: "home-1"`, `version: "1.2.3"`, `listener_addresses: ["http://+:8080"]`
- **THEN** the control plane SHALL register the instance with those values and the gRPC peer IP, not fabricated placeholders

### Requirement: Data plane gRPC connections authenticated by shared secret

When the control plane has `DataPlane:SharedSecrets` configured (non-empty list), it SHALL reject any data plane gRPC connection — `ConfigReplication`, `HeartbeatCollect`, `EventSubscription` — whose `x-dp-secret` metadata does not match one of the configured secrets, failing closed with `PermissionDenied`. When no secrets are configured, connections SHALL be accepted unchanged (local/single-host default). The data plane SHALL attach `x-dp-secret` from `ControlPlane:SharedSecret` to all control-plane gRPC calls when configured.

#### Scenario: Connection without secret rejected when auth enabled

- **WHEN** `DataPlane:SharedSecrets` is non-empty and a data plane connects without a matching `x-dp-secret` metadata
- **THEN** the control plane SHALL reject the connection with `PermissionDenied` before any config snapshot is sent

#### Scenario: No secrets configured means no auth

- **WHEN** `DataPlane:SharedSecrets` is empty
- **THEN** data plane connections SHALL be accepted without `x-dp-secret`, preserving single-host behavior

