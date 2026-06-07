# Instance Config Query

## Purpose

Define how the control plane queries a data plane instance's current runtime YARP configuration through the existing gRPC EventSubscription bidirectional channel.

## Requirements

### Requirement: CONFIG_QUERY event type

The system SHALL add a new `CONFIG_QUERY` event type to the `EventSubscription` gRPC service. The control plane SHALL send this event to request a data plane's current YARP configuration.

#### Scenario: Control plane sends CONFIG_QUERY to data plane

- **WHEN** the control plane needs to query a data plane's current configuration
- **THEN** it SHALL send an `EventMessage` with `type = CONFIG_QUERY` via the EventSubscription bidirectional stream
- **AND** the event's `data_json` SHALL contain the querier's correlation ID for response matching

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

#### Scenario: Query offline instance

- **WHEN** a user sends `GET /api/instances/dp-offline/config`
- **AND** the target data plane is not connected
- **THEN** the control plane SHALL return `503 Service Unavailable` with message "Instance not connected"

#### Scenario: Query non-existent instance

- **WHEN** a user sends `GET /api/instances/non-existent/config`
- **THEN** the control plane SHALL return `404 Not Found`

### Requirement: Frontend config display

The management console SHALL add a "查看" button in the instance table operation column for viewing the currently applied YARP configuration of an instance. The config SHALL be displayed in a modal with human-friendly structured tables, not raw JSON.

#### Scenario: View config button in operation column

- **WHEN** user views the instance list page
- **THEN** each instance row SHALL have a "查看" text button before the "删除" button in the operation column
- **AND** the button SHALL use `.btn-text-primary` class (per button-visibility spec for table operation columns)
- **AND** the button SHALL call `GET /api/instances/{instanceId}/config`

#### Scenario: Config modal shows Routes panel

- **WHEN** user clicks "查看" on an online instance
- **THEN** a modal SHALL open with the title "实例配置 - {instanceName}"
- **AND** the modal header SHALL show the config version and instance address
- **AND** the first section SHALL be a "路由规则 (Routes)" panel with a table:
  - 列: 路由ID, 目标集群, 匹配路径, 匹配域名, 状态
  - 状态列用 badge 显示"启用"(success) / "禁用"(secondary)
  - 空路由时显示"暂无路由规则"

#### Scenario: Config modal shows Clusters panel

- **WHEN** user views the config modal
- **THEN** the second section SHALL be a "目标集群 (Clusters)" panel
- **AND** each cluster SHALL be displayed as a card containing:
  - 集群名称 (ClusterId)
  - 负载均衡策略 (LoadBalancingPolicy)，如 `RoundRobin` → "轮询"
  - 目标地址列表（每个地址一行，显示 address 和健康状态 badge）

#### Scenario: Config modal loading state

- **WHEN** user clicks "查看" and the request is in flight
- **THEN** the button SHALL show a loading indicator or be disabled

#### Scenario: Modal displays error for offline instance

- **WHEN** user clicks "查看" on an offline/heartbeat-timeout instance
- **THEN** the modal SHALL display an error banner: "实例当前未连接，无法获取配置"
- **AND** the content area SHALL be empty

#### Scenario: Modal close

- **WHEN** user clicks the X button or clicks outside the modal
- **THEN** the modal SHALL close

#### Scenario: Config modal re-query

- **WHEN** the config modal is open and user clicks "刷新"
- **THEN** the system SHALL re-send `GET /api/instances/{instanceId}/config` and update the display
