# in-memory-instance-store Specification Delta

## ADDED Requirements

### Requirement: Instance access group assignment

The system SHALL resolve an instance's access group from the `InstanceGroupAssignments` table at registration (gRPC subscription or REST register). Assignment changes via `PUT /api/instances/{id}/group` SHALL update the in-memory record immediately — no heartbeat-time re-resolution, since REST is the only write path. Instances without an assignment SHALL have an empty group.

#### Scenario: Assignment applied on registration
- **WHEN** an instance with `InstanceId: "home-1"` registers and an assignment row maps `home-1` to a group
- **THEN** the instance's `Group` SHALL be that group's Id and Name

#### Scenario: Unassigned instance has empty group
- **WHEN** no assignment row exists for the instance's `InstanceId`
- **THEN** the instance's `Group` SHALL be empty (displayed as 未分组)

#### Scenario: Reassignment survives control plane restart
- **WHEN** the control plane restarts and an instance reconnects
- **THEN** the instance's `Group` SHALL be resolved again from the persisted assignment table

### Requirement: Assign instance group via REST

The system SHALL expose `PUT /api/instances/{id}/group` accepting `{groupId}` or `null` to unassign.

#### Scenario: Assign instance to a group
- **WHEN** a client sends `PUT /api/instances/home-1/group` with a valid `groupId`
- **THEN** the system SHALL persist the assignment and the instance list SHALL show the new group

#### Scenario: Unassign instance
- **WHEN** a client sends `PUT /api/instances/home-1/group` with `null`
- **THEN** the system SHALL remove the assignment and the instance SHALL show as 未分组

#### Scenario: Assignment rejects unknown group or instance
- **WHEN** the `groupId` does not exist or the instance does not exist
- **THEN** the system SHALL return `400` or `404` respectively and SHALL NOT change any assignment

## MODIFIED Requirements

### Requirement: In-memory instance query

#### Scenario: List response carries access group
- **WHEN** a client sends `GET /api/instances`
- **THEN** each item in the response list SHALL include its resolved group Id and Name (empty when unassigned)

### Requirement: Frontend instance display

#### Scenario: Display access group column, filter, and inline assignment
- **WHEN** a user navigates to the Instances page
- **THEN** the table SHALL include an 接入组 column (empty group displayed as 未分组)
- **AND** the page SHALL provide a group filter; selecting a group SHALL show only instances of that group
- **AND** each row SHALL provide a group selector (including 未分组 and a create-group entry) that calls `PUT /api/instances/{id}/group`
