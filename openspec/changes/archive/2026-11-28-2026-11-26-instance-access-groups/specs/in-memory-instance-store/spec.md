# in-memory-instance-store Specification Delta

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: In-memory instance query

#### Scenario: List response carries bound node
- **WHEN** a client sends `GET /api/instances`
- **THEN** each item in the response list SHALL include its resolved node Id and Name (empty when unbound)

### Requirement: Frontend instance display

#### Scenario: Display node column, filter, and inline binding
- **WHEN** a user navigates to the Instances page
- **THEN** the table SHALL include an 接入节点 column (unbound displayed as 未绑定)
- **AND** the page SHALL provide a node filter; selecting a node SHALL show only instances of that node
- **AND** each row SHALL provide a node selector (including 未绑定 and a create-node entry) that calls `PUT /api/instances/{id}/node`
