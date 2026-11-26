# gRPC Config Replication Specification Delta

## MODIFIED Requirements

### Requirement: Data plane receives config updates via gRPC bidirectional stream

The control plane SHALL resolve the connected data plane's access group and SHALL assemble the `ConfigSnapshot` per connection:

- routes bound to a Cloudflare tunnel SHALL be included for every data plane (tunnel ingress backhauls to data planes);
- routes bound to `local` SHALL be included for every data plane (legacy global behavior);
- routes bound to `group:<G>` SHALL be included only when `G` equals the data plane's access group;
- a data plane without an access group SHALL receive no group-bound routes;
- certificates SHALL be filtered to domains referenced by the included routes (same wildcard matching rule as the data plane SNI selector); a data plane without an access group SHALL receive certificates only for `local`/tunnel route domains.

Clusters SHALL be included unchanged.

#### Scenario: Data plane receives filtered config on connect

- **WHEN** a data plane with group `华东` connects to `ConfigReplication.ReplicateConfig`
- **THEN** the control plane sends a `ConfigSnapshot` containing all tunnel-bound and `local` routes plus `group:华东` routes, and certificates for the referenced domains only

#### Scenario: Ungrouped data plane receives no group routes

- **WHEN** a data plane without an access group connects
- **THEN** the `ConfigSnapshot` SHALL contain tunnel-bound and `local` routes only, and no `group:*` routes

#### Scenario: Config change pushes per-connection snapshots

- **WHEN** a user creates/updates/deletes a route, cluster, or certificate via the control plane REST API
- **THEN** the control plane SHALL assemble and push a `ConfigSnapshot` per connected data plane, filtered by that plane's access group, via the bidirectional stream

#### Scenario: Group-bound route change reaches only the matching group

- **WHEN** a route bound to `group:华东` is created or updated
- **THEN** data planes of group `华东` SHALL receive a snapshot including the route, and data planes of other groups SHALL receive a snapshot without it

#### Scenario: Certificate snapshot respects group isolation

- **WHEN** the control plane pushes a snapshot to a data plane of group `华东`
- **THEN** the snapshot's certificates SHALL include only certificates whose domain matches a host referenced by the routes in that snapshot
