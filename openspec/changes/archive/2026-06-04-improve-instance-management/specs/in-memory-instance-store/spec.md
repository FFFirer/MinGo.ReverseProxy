## ADDED Requirements

### Requirement: In-memory instance registration

The system SHALL allow gateway instances to be registered via:
- gRPC `ConfigReplication` stream connection (自动注册)
- REST API `POST /api/instances/register` (手动注册)

Each instance SHALL have a unique `InstanceId`. If not provided by the client, the system SHALL auto-generate one.

#### Scenario: Auto-register on gRPC connect
- **WHEN** a data plane establishes a `ConfigReplication` gRPC bidirectional stream
- **THEN** the system SHALL create a `GatewayInstance` record with status `Online`

#### Scenario: Manual register via REST API
- **WHEN** a client sends `POST /api/instances/register` with valid payload
- **THEN** the system SHALL return `201 Created` with the registered instance

#### Scenario: Auto-register with duplicate ID
- **WHEN** a data plane connects with a `data_plane_id` that already exists
- **THEN** the system SHALL update the existing instance's status to `Online` and refresh its heartbeat

### Requirement: In-memory heartbeat update

The system SHALL update instance metrics when receiving heartbeats via:
- gRPC `HeartbeatCollect` stream (实时更新)
- REST API `POST /api/instances/heartbeat` (手动更新)

Updated fields SHALL include: `LastHeartbeat`, `CpuUsage`, `MemoryUsage`, `TotalRequests`, `ErrorRequests`, `IsHealthy`.

#### Scenario: Heartbeat updates instance
- **WHEN** `HeartbeatCollectService` receives a `HeartbeatRequest` with `data_plane_id: "dp-abc123"`, `cpu_usage: 45.2`, `memory_usage: 62.1`
- **THEN** the instance with `InstanceId == "dp-abc123"` SHALL have `CpuUsage` set to `45.2`, `MemoryUsage` set to `62.1`, `LastHeartbeat` updated to current time

#### Scenario: Heartbeat for unknown instance
- **WHEN** `HeartbeatCollectService` receives a heartbeat for an unknown `data_plane_id`
- **THEN** the system SHALL NOT throw an error; it SHALL silently log and ignore

### Requirement: In-memory timeout detection

The system SHALL detect instances that have not sent heartbeats within 30 seconds and mark them as `HeartbeatTimeout`.

#### Scenario: Instance times out
- **WHEN** an instance's `LastHeartbeat` is more than 30 seconds ago
- **THEN** the instance's `Status` SHALL be returned as `HeartbeatTimeout` in `GetInstancesAsync()` response

### Requirement: In-memory instance query

The system SHALL provide query endpoints to retrieve instance information.

#### Scenario: List all instances
- **WHEN** a client sends `GET /api/instances`
- **THEN** the system SHALL return `GatewayInstanceListResponse` with accurate `TotalCount`, `OnlineCount`, `OfflineCount`, `HealthyCount`, `UnhealthyCount` and the full instance list

#### Scenario: Get single instance
- **WHEN** a client sends `GET /api/instances/{id}`
- **THEN** the system SHALL return the matching instance, or `404` if not found

### Requirement: In-memory instance deletion

The system SHALL allow deleting instances from memory.

#### Scenario: Delete instance
- **WHEN** a client sends `DELETE /api/instances/{id}`
- **THEN** the system SHALL remove the instance from memory and return `204 No Content`

#### Scenario: Delete non-existent instance
- **WHEN** a client sends `DELETE /api/instances/{non-existent-id}`
- **THEN** the system SHALL return `404 Not Found`

### Requirement: Frontend instance display

The system SHALL display gateway instances in the management console with metrics.

#### Scenario: Display instance list
- **WHEN** a user navigates to the Instances page
- **THEN** the page SHALL display a table with columns: Name, Address, Status, CPU, Memory, Requests, Errors, Error Rate, Uptime, Last Heartbeat
- **AND** the page SHALL show summary cards with: Total, Online, Offline counts

#### Scenario: Delete instance from UI
- **WHEN** a user clicks the delete button on an instance row
- **THEN** the system SHALL send `DELETE /api/instances/{id}` and remove the instance from the list

#### Scenario: Empty instance list
- **WHEN** there are no registered instances
- **THEN** the page SHALL display "暂无实例" with a visual hint
