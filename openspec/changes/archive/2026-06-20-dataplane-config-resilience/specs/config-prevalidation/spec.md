## ADDED Requirements

### Requirement: ConfigSnapshot pre-validation before YARP application

The data plane SHALL validate every received `ConfigSnapshot` in `DataPlaneConfigProvider.ApplyConfig()` before replacing the current configuration. The validation SHALL check for fatal errors that would cause YARP to throw exceptions. If validation fails, the data plane SHALL discard the snapshot, keep the current (old) configuration, and log a Warning with all validation errors.

#### Scenario: Valid config snapshot is applied

- **WHEN** the data plane receives a `ConfigSnapshot` where all routes have non-empty `RouteId` and `ClusterId`, all `ClusterId` values reference an existing cluster in the snapshot, all destination addresses are valid URIs, the `LoadBalancingPolicy` is in the set `RoundRobin|LeastRequests|PowerOfTwoChoices|FirstAlphabetical|Random`, and no duplicate `RouteId` exists
- **THEN** the data plane SHALL apply the snapshot to `DataPlaneProxyConfig`, signal the `IChangeToken`, and log an Informational message with version, route count, and cluster count

#### Scenario: Config snapshot with empty RouteId is rejected

- **WHEN** the data plane receives a `ConfigSnapshot` containing a route with an empty or null `RouteId`
- **THEN** the data plane SHALL NOT replace the current configuration, SHALL log a Warning listing the validation error, and SHALL keep the old configuration active

#### Scenario: Config snapshot with missing ClusterId reference is rejected

- **WHEN** the data plane receives a `ConfigSnapshot` where a route's `ClusterId` does not match any `ClusterConfig.Id` in the same snapshot
- **THEN** the data plane SHALL NOT replace the current configuration and SHALL log a Warning

#### Scenario: Config snapshot with invalid destination address is rejected

- **WHEN** the data plane receives a `ConfigSnapshot` containing a destination whose `Address` is not a valid URI (fails `Uri.TryCreate` with `UriKind.Absolute`)
- **THEN** the data plane SHALL NOT replace the current configuration and SHALL log a Warning

#### Scenario: Config snapshot with invalid LoadBalancingPolicy is rejected

- **WHEN** the data plane receives a `ConfigSnapshot` containing a cluster whose `LoadBalancingPolicy` is not in the allowed set (`RoundRobin|LeastRequests|PowerOfTwoChoices|FirstAlphabetical|Random`) and is not empty
- **THEN** the data plane SHALL NOT replace the current configuration and SHALL log a Warning

#### Scenario: Config snapshot with duplicate RouteId is rejected

- **WHEN** the data plane receives a `ConfigSnapshot` containing two or more routes with the same `RouteId`
- **THEN** the data plane SHALL NOT replace the current configuration and SHALL log a Warning

#### Scenario: Config snapshot with older version is rejected

- **WHEN** the data plane receives a `ConfigSnapshot` whose `Version` is less than or equal to the current config version
- **THEN** the data plane SHALL discard the snapshot and log a Debug message (not a Warning, since this is expected during reconnection)

#### Scenario: Config snapshot with empty ClusterId is rejected

- **WHEN** the data plane receives a `ConfigSnapshot` containing a cluster with an empty or null `Id`
- **THEN** the data plane SHALL NOT replace the current configuration and SHALL log a Warning

### Requirement: Config loading status exposed via health check

The data plane SHALL expose a `/healthz/ready` endpoint that reflects whether the configuration has been successfully applied at least once. The endpoint SHALL return HTTP 200 when config has been applied successfully, and HTTP 503 when no config has been successfully applied yet.

#### Scenario: Health check returns healthy after first successful config apply

- **WHEN** the data plane has successfully applied at least one config snapshot via `ApplyConfig()`
- **THEN** `GET /healthz/ready` SHALL return HTTP 200

#### Scenario: Health check returns unhealthy before any config is applied

- **WHEN** the data plane has not yet received or applied any config snapshot
- **THEN** `GET /healthz/ready` SHALL return HTTP 503

#### Scenario: Health check returns healthy when config was applied then failed

- **WHEN** the data plane successfully applied a config snapshot, then received a subsequent snapshot that failed validation
- **THEN** `GET /healthz/ready` SHALL return HTTP 200 (the last successful config is still active)

### Requirement: Startup does not block on initial config

The data plane SHALL NOT block startup waiting for the initial configuration from the control plane. The `WaitForInitialConfigAsync` call SHALL be removed from the startup path. The data plane SHALL start serving requests immediately with an empty configuration (zero routes, zero clusters).

#### Scenario: Data plane starts without waiting for config

- **WHEN** the data plane process starts
- **THEN** it SHALL begin listening on its configured ports immediately without waiting for a gRPC config snapshot
- **AND** requests received before any config is loaded SHALL receive HTTP 503 (no matching routes)

#### Scenario: Config arrives after startup and is hot-loaded

- **WHEN** the data plane receives its first config snapshot after startup
- **THEN** it SHALL apply the config via `ApplyConfig()`, signal YARP's `IChangeToken`, and begin routing requests according to the new config without restart
