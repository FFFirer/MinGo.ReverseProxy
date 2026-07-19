# auto-destination-id Specification (Delta)

## MODIFIED Requirements

### Requirement: Auto-generate destination ID

The system SHALL auto-generate a unique ID for each destination when it is added to a cluster. The ID format SHALL be `{clusterId}-{sequence}`.

**变更说明**: `{clusterId}` 从原来的 GUID 变为用户可读的集群名称，使目标 ID 更可读。

#### Scenario: Destination IDs use cluster name
- **WHEN** cluster `prod-api` is created with destinations `["http://srv1:8080", "http://srv2:8080"]`
- **THEN** destinations SHALL get IDs `prod-api-1` and `prod-api-2` respectively
