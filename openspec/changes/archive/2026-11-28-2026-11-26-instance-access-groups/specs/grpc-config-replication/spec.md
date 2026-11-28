# gRPC Config Replication Specification Delta

## MODIFIED Requirements

### Requirement: Data plane receives config updates via gRPC bidirectional stream

The control plane SHALL resolve the connected data plane's bound node and SHALL assemble the `ConfigSnapshot` per connection:

- routes bound to a Cloudflare tunnel SHALL be included for every data plane that is bound to a node (tunnel ingress backhauls to data planes);
- routes bound to `node:<N>` SHALL be included only when `N` equals the data plane's bound node;
- a data plane without a bound node SHALL receive **no config at all** — the control plane SHALL NOT send a `ConfigSnapshot` on registration, and SHALL skip it during broadcasts;
- when a data plane is unbound, the control plane SHALL push an empty snapshot (no routes, no certificates) to clear previously installed rules;
- certificates SHALL be filtered to domains referenced by the included routes (same wildcard matching rule as the data plane SNI selector); an empty snapshot carries no certificates.

Clusters SHALL be included unchanged.

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
