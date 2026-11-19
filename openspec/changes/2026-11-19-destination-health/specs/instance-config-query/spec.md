## MODIFIED Requirements

### Requirement: CONFIG_REPORT event type

The system SHALL add a new `CONFIG_REPORT` event type to the `EventSubscription` gRPC service. Data planes SHALL respond to `CONFIG_QUERY` with their current configuration.

#### Scenario: Data plane responds with current config

- **WHEN** a data plane receives a `CONFIG_QUERY` event
- **THEN** it SHALL read its current configuration from `DataPlaneConfigProvider.GetConfig()`
- **AND** it SHALL send an `EventMessage` with `type = CONFIG_REPORT` containing:
  - `event_id`: same as the query event's ID (correlation)
  - `data_json`: serialized object with `routes` and `clusters` arrays

#### Scenario: CONFIG_REPORT contains runtime routes

- **WHEN** a data plane reports its configuration
- **THEN** the `routes` field SHALL contain each route's `RouteId`, `ClusterId`, `Match` (path, hosts)
- **AND** each route SHALL reflect the data plane's actual runtime state as returned by `DataPlaneConfigProvider`

#### Scenario: CONFIG_REPORT contains runtime clusters

- **WHEN** a data plane reports its configuration
- **THEN** the `clusters` field SHALL contain each cluster's `ClusterId`, `LoadBalancingPolicy`, and `Destinations` (id, address, healthy)
- **AND** each cluster SHALL reflect the data plane's actual runtime state as returned by `DataPlaneConfigProvider`
- **AND** each destination's `healthy` SHALL be the YARP runtime health state folded per the `destination-health` capability, not a hardcoded `true`

#### Scenario: Config query timeout

- **WHEN** the control plane sends a `CONFIG_QUERY` but receives no `CONFIG_REPORT` within 10 seconds
- **THEN** the control plane SHALL return a timeout error to the API caller

### Requirement: Control plane config query API

The system SHALL expose `GET /api/instances/{id}/config` on the control plane API. This endpoint SHALL query the target data plane's current configuration and return it.

#### Scenario: Query online instance config

- **WHEN** a user sends `GET /api/instances/dp-abc123/config`
- **AND** data plane `dp-abc123` is connected via EventSubscription
- **THEN** the control plane SHALL send a `CONFIG_QUERY` event to that instance
- **AND** SHALL return the runtime `routes` and `clusters` when the `CONFIG_REPORT` response arrives
- **AND** SHALL include `version` from the config snapshot's version number
- **AND** destination health values SHALL be the data plane's runtime state per the `destination-health` capability

#### Scenario: Query offline instance

- **WHEN** a user sends `GET /api/instances/dp-offline/config`
- **AND** the target data plane is not connected
- **THEN** the control plane SHALL return `503 Service Unavailable` with message "Instance not connected"

#### Scenario: Query non-existent instance

- **WHEN** a user sends `GET /api/instances/non-existent/config`
- **THEN** the control plane SHALL return `404 Not Found`
