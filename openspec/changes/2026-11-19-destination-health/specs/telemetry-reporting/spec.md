## ADDED Requirements

### Requirement: Heartbeat SHALL carry destination health states from YARP runtime

The heartbeat SHALL include a `destination_health` section carrying, for each cluster destination in the applied config, the data plane's runtime health state folded to a boolean, per the `destination-health` capability: a destination whose active or passive health is `Unhealthy` SHALL be reported as unhealthy; all other states (including `Unknown`) SHALL be reported as healthy. The section SHALL be sourced from YARP's `IClusterDestinationsState`, never from the static config flag.

#### Scenario: Unhealthy destination appears in heartbeat

- **WHEN** YARP's active health check marks a destination unhealthy
- **THEN** the next heartbeat contains a `destination_health` entry for that cluster/destination with `is_healthy = false`

#### Scenario: Heartbeat from a legacy data plane carries no health section

- **WHEN** a data plane build predating `destination_health` sends a heartbeat
- **THEN** the `destination_health` list is empty
- **AND** the control plane SHALL leave persisted destination health values unchanged

#### Scenario: Health states come from runtime, not config

- **WHEN** a destination's static config flag is `true` but YARP has marked it unhealthy
- **THEN** the heartbeat SHALL report `is_healthy = false`

### Requirement: Control plane SHALL persist destination health only on change

The control plane SHALL aggregate the `destination_health` reports from all connected data planes per the `destination-health` capability and SHALL write to the database only when the aggregated value differs from the persisted value. Heartbeats carrying an unchanged aggregated value MUST NOT cause database writes.

#### Scenario: No write when aggregated state is unchanged

- **WHEN** a heartbeat's aggregated health for a destination equals the persisted value
- **THEN** the control plane performs no database update for that destination

#### Scenario: Aggregated state flips trigger a write

- **WHEN** the fail-closed aggregate for a destination changes
- **THEN** the control plane updates the persisted `Healthy` value once
