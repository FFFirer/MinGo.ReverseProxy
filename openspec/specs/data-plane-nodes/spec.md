# data-plane-nodes Specification

## Purpose

数据面节点作为落库的接入通道资产：节点的增删改查、默认节点保障、实例绑定，以及路由通道校验与路由表单的通道选择口径。

## Requirements

### Requirement: Data plane nodes as managed channels

The system SHALL persist data plane nodes (Id, Name, timestamps) in the database and expose CRUD endpoints under `/api/data-plane-nodes`. The node list SHALL include per-node registered and online instance counts. A default node with Id `default` SHALL exist (created by migration and ensured at startup).

#### Scenario: Default node exists
- **WHEN** the control plane starts on a fresh or migrated database
- **THEN** a node with Id `default` and name 默认节点 SHALL exist

#### Scenario: Create a node
- **WHEN** a client sends `POST /api/data-plane-nodes` with a name
- **THEN** the system SHALL create the node and return it with its Id

#### Scenario: List nodes with instance counts
- **WHEN** a client sends `GET /api/data-plane-nodes`
- **THEN** the system SHALL return every node with its total and online instance counts

#### Scenario: Rename a node
- **WHEN** a client sends `PUT /api/data-plane-nodes/{id}` with a new name
- **THEN** routes referencing `node:{id}` SHALL keep working (reference is by Id, not name)

#### Scenario: Delete an unused node
- **WHEN** a client sends `DELETE /api/data-plane-nodes/{id}` and no route references it
- **THEN** the system SHALL delete the node and its instance assignments

#### Scenario: Delete the default node is refused
- **WHEN** a client sends `DELETE /api/data-plane-nodes/default`
- **THEN** the system SHALL return `400` and SHALL NOT delete the node

#### Scenario: Delete a referenced node is refused
- **WHEN** a client sends `DELETE /api/data-plane-nodes/{id}` and routes reference `node:{id}`
- **THEN** the system SHALL return `409` with the referencing route names and SHALL NOT delete the node

### Requirement: Nodes maintained from both console pages

The node channel SHALL be maintainable from the 接入通道 page (list/add/rename/delete with member counts) and from the 实例管理 page (inline create and per-row binding), both operating on the same endpoints and data.

#### Scenario: Connections page shows node channels
- **WHEN** a user opens the 接入通道 page
- **THEN** the page SHALL list data plane node channels alongside Cloudflare tunnel channels, with member counts and add/rename/delete actions; the default node SHALL be marked 默认 and its delete action disabled

#### Scenario: Instances page creates and binds nodes
- **WHEN** a user binds an instance from the Instances page or creates a node inline
- **THEN** the change SHALL be visible on the 接入通道 page immediately (same persisted data)

### Requirement: Route channel validation

When saving a route, the system SHALL validate its `ConnectionId`: `node:<id>` SHALL reference an existing node, tunnel IDs SHALL reference an existing tunnel. Invalid channels SHALL be rejected with `400`. The legacy `local` channel SHALL no longer exist (migrated to `node:default`).

#### Scenario: Route saved with unknown node channel
- **WHEN** a client saves a route with `ConnectionId: "node:missing"`
- **THEN** the system SHALL return `400` and SHALL NOT save the route

#### Scenario: Route saved with valid node channel
- **WHEN** a client saves a route with `ConnectionId: "node:{existing-id}"`
- **THEN** the system SHALL save the route and it SHALL be delivered only to data planes bound to that node

### Requirement: Unified channel selection on route form

The route form SHALL offer a single 接入通道 dropdown listing data plane nodes and Cloudflare tunnels. The legacy `local` option SHALL NOT be offered anywhere.

#### Scenario: Route channel dropdown
- **WHEN** a user creates or edits a route
- **THEN** the channel dropdown SHALL list node channels (default node included) and tunnels, and SHALL NOT list `local`