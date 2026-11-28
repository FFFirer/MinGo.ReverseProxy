# in-memory-instance-store Specification

## Purpose
TBD - created by archiving change improve-instance-management. Update Purpose after archive.
## Requirements
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

### Requirement: Registration uses agent self-description

When a data plane registers via gRPC `ConfigReplication`, the system SHALL populate `Name`, `Version`, and `ListenerAddresses` from the subscription's `name`/`version`/`listener_addresses` fields, and `IpAddress` from the gRPC connection peer. Empty subscription fields SHALL fall back to `DataPlane-{id prefix}` (name) and `unknown` (version).

#### Scenario: Instance shows real agent metadata
- **WHEN** a data plane subscribes with `name: "home-1"`, `version: "1.2.3"`, `listener_addresses: ["http://+:8080"]`
- **THEN** the registered instance SHALL have those values instead of fabricated placeholders

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

### Requirement: Instance node binding

The system SHALL resolve an instance's bound node from the `InstanceNodeAssignments` table at registration (gRPC subscription or REST register). Binding changes via `PUT /api/instances/{id}/node` SHALL update the in-memory record immediately — no heartbeat-time re-resolution, since REST is the only write path. Instances without an assignment SHALL have an empty node.

#### Scenario: Binding applied on registration
- **WHEN** an instance with `InstanceId: "home-1"` registers and an assignment row maps `home-1` to a node
- **THEN** the instance's `Node` SHALL be that node's Id and Name

#### Scenario: Unbound instance has empty node
- **WHEN** no assignment row exists for the instance's `InstanceId`
- **THEN** the instance's `Node` SHALL be empty (displayed as 未绑定)

#### Scenario: Reassignment survives control plane restart
- **WHEN** the control plane restarts and an instance reconnects
- **THEN** the instance's `Node` SHALL be resolved again from the persisted assignment table

### Requirement: Bind instance node via REST

The system SHALL expose `PUT /api/instances/{id}/node` accepting `{nodeId}` or `null` to unbind.

#### Scenario: Bind instance to a node
- **WHEN** a client sends `PUT /api/instances/home-1/node` with a valid `nodeId`
- **THEN** the system SHALL persist the binding and the instance list SHALL show the new node

#### Scenario: Unbind instance
- **WHEN** a client sends `PUT /api/instances/home-1/node` with `null`
- **THEN** the system SHALL remove the binding and the instance SHALL show as 未绑定

#### Scenario: Binding rejects unknown node or instance
- **WHEN** the `nodeId` does not exist or the instance does not exist
- **THEN** the system SHALL return `400` or `404` respectively and SHALL NOT change any binding

### Requirement: In-memory instance query

The system SHALL provide query endpoints to retrieve instance information.

#### Scenario: List all instances
- **WHEN** a client sends `GET /api/instances`
- **THEN** the system SHALL return `GatewayInstanceListResponse` with accurate `TotalCount`, `OnlineCount`, `OfflineCount`, `HealthyCount`, `UnhealthyCount` and the full instance list

#### Scenario: List response carries bound node
- **WHEN** a client sends `GET /api/instances`
- **THEN** each item in the response list SHALL include its resolved node Id and Name (empty when unbound)

#### Scenario: Get single instance
- **WHEN** a client sends `GET /api/instances/{id}`
- **THEN** the system SHALL return the matching instance, or `404` if not found

#### Scenario: Query instance runtime config
- **WHEN** a client sends `GET /api/instances/{id}/config`
- **THEN** the system SHALL return the data plane's current YARP runtime configuration (`routes` and `clusters`)
- **OR** return `503` if the instance is not connected
- **OR** return `404` if the instance does not exist

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

#### Scenario: Display node column, filter, and inline binding
- **WHEN** a user navigates to the Instances page
- **THEN** the table SHALL include an 接入节点 column (unbound displayed as 未绑定)
- **AND** the page SHALL provide a node filter; selecting a node SHALL show only instances of that node
- **AND** each row SHALL provide a node selector (including 未绑定 and a create-node entry) that calls `PUT /api/instances/{id}/node`

