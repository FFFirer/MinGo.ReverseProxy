# instance-groups Specification Delta

## ADDED Requirements

### Requirement: Data plane groups as managed channels

The system SHALL persist data plane groups (Id, Name, timestamps) in the database and expose CRUD endpoints under `/api/instance-groups`. The group list SHALL include per-group registered and online instance counts.

#### Scenario: Create a group
- **WHEN** a client sends `POST /api/instance-groups` with a name
- **THEN** the system SHALL create the group and return it with its Id

#### Scenario: List groups with instance counts
- **WHEN** a client sends `GET /api/instance-groups`
- **THEN** the system SHALL return every group with its total and online instance counts

#### Scenario: Rename a group
- **WHEN** a client sends `PUT /api/instance-groups/{id}` with a new name
- **THEN** routes referencing `group:{id}` SHALL keep working (reference is by Id, not name)

#### Scenario: Delete an unused group
- **WHEN** a client sends `DELETE /api/instance-groups/{id}` and no route references it
- **THEN** the system SHALL delete the group and its instance assignments

#### Scenario: Delete a referenced group is refused
- **WHEN** a client sends `DELETE /api/instance-groups/{id}` and routes reference `group:{id}`
- **THEN** the system SHALL return `409` with the referencing route names and SHALL NOT delete the group

### Requirement: Groups maintained from both console pages

The group channel SHALL be maintainable from the 接入通道 page (list/add/rename/delete with member counts) and from the 实例管理 page (inline create and per-row assignment), both operating on the same endpoints and data.

#### Scenario: Connections page shows group channels
- **WHEN** a user opens the 接入通道 page
- **THEN** the page SHALL list data plane group channels alongside Cloudflare tunnel channels, with member counts and add/rename/delete actions

#### Scenario: Instances page creates and assigns groups
- **WHEN** a user assigns an instance from the Instances page or creates a group inline
- **THEN** the change SHALL be visible on the 接入通道 page immediately (same persisted data)

### Requirement: Route channel validation

When saving a route, the system SHALL validate its `ConnectionId`: `group:<id>` SHALL reference an existing group, tunnel IDs SHALL reference an existing tunnel. Invalid channels SHALL be rejected with `400`.

#### Scenario: Route saved with unknown group channel
- **WHEN** a client saves a route with `ConnectionId: "group:missing"`
- **THEN** the system SHALL return `400` and SHALL NOT save the route

#### Scenario: Route saved with valid group channel
- **WHEN** a client saves a route with `ConnectionId: "group:{existing-id}"`
- **THEN** the system SHALL save the route and it SHALL be delivered only to data planes of that group

### Requirement: Unified channel selection on route form

The route form SHALL offer a single 接入通道 dropdown listing data plane groups and Cloudflare tunnels. New routes SHALL NOT offer the legacy `local` option; existing `local` routes SHALL display as 本地（全部，存量）.

#### Scenario: New route channel dropdown
- **WHEN** a user creates a route
- **THEN** the channel dropdown SHALL list group channels and tunnels, and SHALL NOT list `local`

#### Scenario: Legacy local route editing
- **WHEN** a user edits an existing `local` route
- **THEN** the form SHALL show 本地（全部，存量） as its channel without offering it as a selectable option
