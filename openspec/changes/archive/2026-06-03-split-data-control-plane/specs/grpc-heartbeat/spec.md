## ADDED Requirements

### Requirement: Data plane reports heartbeat and metrics via gRPC bidirectional stream

The control plane SHALL expose a gRPC `HeartbeatCollect` service with a `ReportHeartbeat` bidirectional streaming RPC. The data plane SHALL periodically send `HeartbeatRequest` messages containing: instance ID, CPU usage, memory usage, total requests, error requests, health status, and a batch of `MetricPoint` values. The control plane SHALL respond with a `HeartbeatResponse` confirming receipt and optionally containing `ControlCommand` messages for the data plane.

#### Scenario: Data plane sends heartbeat at configured interval
- **WHEN** the data plane's heartbeat timer expires (default 10 seconds)
- **THEN** it sends a `HeartbeatRequest` with current CPU, memory, request stats, and queued metric points

#### Scenario: Control plane acknowledges heartbeat
- **WHEN** the control plane receives a `HeartbeatRequest`
- **THEN** it returns a `HeartbeatResponse` with `ack: true` and updates the instance's last heartbeat timestamp

#### Scenario: Control plane sends reload command via heartbeat response
- **WHEN** the control plane needs to force a data plane config reload
- **THEN** it includes a `ControlCommand` with `CMD_RELOAD_CONFIG` in the `HeartbeatResponse`

#### Scenario: Data plane executes control commands from heartbeat response
- **WHEN** the data plane receives a `HeartbeatResponse` containing `ControlCommand` entries
- **THEN** it executes each command (reload config, reload certs, etc.) and logs the result

### Requirement: Control plane detects data plane heartbeat timeout

The control plane SHALL track the last heartbeat time for each connected data plane instance. If no heartbeat is received within a configurable timeout (default 30 seconds), the control plane SHALL mark the instance as `HeartbeatTimeout`. If timeout exceeds a configurable threshold (default 120 seconds), the control plane SHALL mark the instance as `Offline`.

#### Scenario: Control plane marks instance as timed out
- **WHEN** a data plane instance does not send a heartbeat for 30 seconds
- **THEN** the control plane changes its status to `HeartbeatTimeout`

#### Scenario: Control plane marks instance as offline
- **WHEN** a data plane instance does not send a heartbeat for 120 seconds
- **THEN** the control plane changes its status to `Offline`
