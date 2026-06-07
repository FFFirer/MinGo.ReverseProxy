# in-memory-instance-store Specification

## MODIFIED Requirements

### Requirement: In-memory instance query

The system SHALL provide query endpoints to retrieve instance information **and their current runtime configuration**.

#### Scenario: List all instances
- **WHEN** a client sends `GET /api/instances`
- **THEN** the system SHALL return `GatewayInstanceListResponse` with accurate `TotalCount`, `OnlineCount`, `OfflineCount`, `HealthyCount`, `UnhealthyCount` and the full instance list

#### Scenario: Get single instance
- **WHEN** a client sends `GET /api/instances/{id}`
- **THEN** the system SHALL return the matching instance, or `404` if not found

#### Scenario: Query instance runtime config
- **WHEN** a client sends `GET /api/instances/{id}/config`
- **THEN** the system SHALL return the data plane's current YARP runtime configuration (`routes` and `clusters`)
- **OR** return `503` if the instance is not connected
- **OR** return `404` if the instance does not exist
