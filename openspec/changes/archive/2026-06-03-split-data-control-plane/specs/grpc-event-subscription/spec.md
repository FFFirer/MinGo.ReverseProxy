## ADDED Requirements

### Requirement: Bidirectional event streaming between control plane and data plane

The control plane SHALL expose a gRPC `EventSubscription` service with a `SubscribeEvents` bidirectional streaming RPC. Both control plane and data plane SHALL be able to send `EventStream` messages at any time after the stream is established. Each `EventStream` message SHALL contain: event ID, event type (ConfigUpdate, InstanceStatusChange, HealthStatusChange, ErrorAlert, CertificateUpdate), source identifier, JSON payload, and timestamp.

#### Scenario: Data plane reports error alert to control plane
- **WHEN** the data plane encounters a proxy error (e.g., destination unreachable)
- **THEN** it sends an `EventStream` with type `ERROR_ALERT` and error details in `data_json`

#### Scenario: Control plane pushes instance status change event
- **WHEN** the control plane detects a data plane instance status change (online/timeout/offline)
- **THEN** it sends an `EventStream` with type `INSTANCE_STATUS_CHANGE` to all connected data plane instances

#### Scenario: Data plane receives certificate update notification
- **WHEN** a certificate is created or updated on the control plane
- **THEN** the control plane sends an `EventStream` with type `CERTIFICATE_UPDATE` to trigger certificate reload on the data plane

### Requirement: Event history is persisted in the control plane database

The control plane SHALL persist all events received or generated via the event subscription system in a database table (`GatewayEvents`). Events SHALL have a configurable retention period (default 7 days). The existing `GatewayEvent` entity and `GatewayEventService` SHALL be reused for persistence.

#### Scenario: Event is persisted when published
- **WHEN** an `EventStream` message is sent by either control plane or data plane
- **THEN** the control plane saves it as a `GatewayEvent` record in the database

#### Scenario: Expired events are cleaned up
- **WHEN** a background cleanup task runs (default hourly)
- **THEN** events older than the retention period (7 days) are deleted from the database
