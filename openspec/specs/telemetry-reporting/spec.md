# telemetry-reporting Specification

## Purpose

定义数据面把遥测送到控制面、以及控制面把遥测摆到人眼前的契约：上报覆盖每个留存点而不是抽样最后几个、单周期有界并可顺延、任何丢失都以累计计数可见、心跳标量来自真实来源、访问日志字段跨进程不丢失、控制面读取口径唯一，以及标准化出口就是 OTLP。

上报实现位于 `src/MinGo.DataPlane/Heartbeat/HeartbeatRequestBuilder.cs` 与 `HeartbeatReporter.cs`，
存储侧的序号与分页读取位于 `src/MinGo.Core/Services/TelemetryStore.cs`，
落地与查询位于 `src/MinGo.ControlPlane.Api/GrpcServices/HeartbeatCollectService.cs`、`Controllers/TelemetryController.cs` 与 `src/MinGo.Application/Services/`。
采集侧本身的契约见 `request-telemetry` 与 `request-tracing` 规范。

## Requirements

### Requirement: Heartbeat metric reporting SHALL cover every point retained by the data plane

The heartbeat SHALL report metric points by continuing from a per-metric-name cursor over the points the data plane has produced, rather than by sampling the newest points. Reading the last N points of each metric per cycle is prohibited: it silently converts every control-plane aggregate built on point sums, averages and percentiles into a sample of unknown rate.

`TelemetryStore` SHALL stamp each enqueued point with a per-metric-name monotonically increasing sequence and SHALL expose a bounded read of the points whose sequence is greater than a supplied cursor, oldest first. The heartbeat SHALL advance a cursor only after the request carrying those points has been written successfully.

#### Scenario: A busy interval is reported in full

- **WHEN** more than ten `gateway.requests.total` points are produced between two heartbeats
- **THEN** every one of those points appears in a heartbeat request
- **AND** the control-plane sum of reported point values equals the number of requests the data plane recorded

#### Scenario: Already reported points are not reported twice

- **WHEN** a heartbeat has been written successfully and the next heartbeat is built
- **THEN** the cursor for each metric name is at the newest sequence already sent
- **AND** the next request contains only points produced after it

#### Scenario: A failed write does not skip data

- **WHEN** building a request succeeded but writing it to the stream threw
- **THEN** no cursor advanced
- **AND** the following heartbeat re-reads the same points

### Requirement: Metric extraction SHALL be bounded per cycle and delay rather than drop

The number of points reported per metric name per heartbeat SHALL be capped by a configurable limit (default 2000, key `Telemetry:MaxMetricPointsPerHeartbeat`). Exceeding the cap SHALL leave the remaining points in place for a later cycle. Failing to report within one cycle MUST NOT be recorded as a loss, and MUST NOT be silently discarded.

#### Scenario: Backlog drains over consecutive cycles

- **WHEN** a metric has more points above the per-cycle cap than one heartbeat can carry
- **THEN** the first cycle reports the oldest bounded page
- **AND** the following cycles continue from that cursor until the backlog is empty
- **AND** no dropped-counter value increases as a consequence of the cap alone

### Requirement: Reporting losses SHALL be visible as cumulative counters

Points evicted from the data plane store before they were reported SHALL be counted, derived from the gap between the reporting cursor and the oldest sequence available, and SHALL be surfaced as `gateway.telemetry.unreported.dropped`. Access-log entries discarded by the store's FIFO bound SHALL be counted and surfaced as `gateway.telemetry.access_log.dropped`, alongside the existing `gateway.telemetry.dropped` for metric points evicted by capacity. These self-observing metric names SHALL use a fixed, empty tag set.

All three SHALL carry the cumulative count as the point value, not a per-cycle delta, and SHALL be reported on every heartbeat regardless of whether the count changed.

#### Scenario: Eviction ahead of the cursor is not silent

- **WHEN** points older than the reporting cursor are evicted from the store
- **THEN** the next heartbeat contains a `gateway.telemetry.unreported.dropped` point whose value covers those points
- **AND** the request count reported for that metric remains the count of points actually delivered

#### Scenario: Access log overflow is counted

- **WHEN** access logs are produced faster than the heartbeat drains them and the queue reaches its bound
- **THEN** `gateway.telemetry.access_log.dropped` grows by the number of discarded entries
- **AND** the eviction decision on the request path still uses bookkeeping counters rather than O(n) `ConcurrentQueue.Count`

### Requirement: Self-observing counters MUST NOT enter sum-based aggregation

Per-request metric points and self-observing counter points have different value semantics — the former carry `1` (counts) or one request's duration in milliseconds, the latter carry a cumulative total. Control-plane aggregation that sums point values SHALL apply only to the per-request metric names, and SHALL NOT apply to the self-observing counter names. Introducing a shared tag to distinguish them is prohibited; the distinction is carried by the metric name.

#### Scenario: Percentiles are computed over request durations only

- **WHEN** the control plane computes latency percentiles
- **THEN** the sample set is drawn from `gateway.request.duration` points
- **AND** no `gateway.telemetry.*` counter point contributes to it

### Requirement: Heartbeat request and error totals SHALL be monotonic lifetime counters

`total_requests` and `error_requests` in the heartbeat SHALL be counters accumulated for the lifetime of the data plane process, incremented at the same point where the OTel request counters are incremented. They MUST NOT be derived by summing points within the store's retention window, because window roll-over and capacity eviction make such a number decrease. The counters SHALL keep accumulating when `OpenTelemetry:Enabled=false`.

#### Scenario: Retention roll-over does not move the counter backwards

- **WHEN** points expire out of the 24-hour retention window while no new requests arrive
- **THEN** the reported `total_requests` is unchanged
- **AND** it never decreases between two heartbeats while the process keeps running

#### Scenario: Disabled OTel pipeline keeps counting

- **WHEN** the data plane starts with `OpenTelemetry:Enabled=false` and serves requests
- **THEN** the heartbeat reports non-zero `total_requests` matching the number of recorded requests

### Requirement: System metrics SHALL come from real process measurements

`cpu_usage` SHALL be the fraction of aggregate CPU time the data plane process consumed during the reporting interval, divided by `Environment.ProcessorCount`, expressed as a percentage and clamped to the range 0 to 100. `memory_usage` SHALL be the process working set expressed in megabytes. Fixed placeholder values are prohibited. The sampling SHALL happen behind an injectable seam so that reporting behaviour is testable without a real process.

#### Scenario: CPU is an interval average, not an instantaneous read

- **WHEN** two consecutive samples differ by a known CPU time over a known wall-clock interval
- **THEN** the reported `cpu_usage` equals that delta divided by the processor count and the interval
- **AND** a sample exceeding one full core per second does not report above 100

#### Scenario: Values reach the instance record

- **WHEN** the control plane ingests a heartbeat carrying non-zero system metrics
- **THEN** the gateway instance record stores those values instead of the previous zeros

### Requirement: is_healthy SHALL reflect configuration synchronisation

`is_healthy` SHALL report whether the data plane holds a configuration it has applied successfully, using the same predicate the readiness health check reports. It MUST NOT be a constant.

A snapshot that fails pre-validation is never applied, so the data plane keeps serving the last known good configuration; that case MUST NOT flip the reported health, because readiness and heartbeat would then disagree with what the proxy is actually running.

#### Scenario: No configuration applied successfully yet

- **WHEN** the data plane has not applied any configuration snapshot successfully
- **THEN** the heartbeat reports `is_healthy=false`
- **AND** the readiness endpoint reports the same status

#### Scenario: Configuration is applied

- **WHEN** a configuration snapshot has been applied successfully
- **THEN** the heartbeat reports `is_healthy=true`

#### Scenario: A rejected snapshot does not contradict readiness

- **WHEN** a later snapshot fails pre-validation and is discarded
- **THEN** the heartbeat and the readiness endpoint report the same status
- **AND** neither reports unhealthy solely because a snapshot was rejected

### Requirement: Access log transport SHALL preserve every collected field

The heartbeat's `AccessLogEntry` message SHALL carry the correlation keys and proxy dimensions that the data plane collects, in addition to the fields it already carried. Proto fields SHALL be added by appending new field numbers only; existing field numbers, names and types MUST NOT change. The control plane SHALL store and serve these fields on its access log model.

When a field arrives empty — for example from an older data plane that does not populate it — the control plane SHALL represent it as unknown. It MUST NOT substitute the collection side's `none` sentinel, which is a data plane contract for "measured and absent", not for "not transported".

#### Scenario: Correlation key survives the hop

- **WHEN** a proxied request with a trace id is drained into a heartbeat
- **THEN** the control plane's access log entry for it exposes the same trace id
- **AND** a console user reading that log entry can locate the corresponding trace in the OTLP backend

#### Scenario: Older producer does not fake a dimension

- **WHEN** a heartbeat carries an access log entry whose cluster field is empty
- **THEN** the control plane stores it as unknown rather than as `none`

### Requirement: Control-plane request counting SHALL use one caliber and expose the instance dimension

Every control-plane query endpoint that reports request or error totals SHALL derive them by summing per-request metric point values, consistent with the monitoring service; using the number of stored points as a count is prohibited. Reading tag values from an `Activity` SHALL enumerate tag objects rather than rely on the string-only tag projection, so that integer and floating point attributes are not read as absent. Metric points ingested from data planes carry an `instance_id` tag, and the telemetry query endpoints SHALL accept an optional instance filter for it.

#### Scenario: Point count is never mistaken for request count

- **WHEN** the stats endpoint answers for a period containing multi-valued duration points and unit-valued total points
- **THEN** `totalRequests` equals the sum of `gateway.requests.total` point values
- **AND** it matches what the monitoring summary reports for the same store contents

#### Scenario: Non-string tags are readable

- **WHEN** a stored activity carries an integer status attribute
- **THEN** the trace projection returns that value instead of null

#### Scenario: One instance can be isolated

- **WHEN** the telemetry stats endpoint is called with an instance filter
- **THEN** only points tagged with that `instance_id` contribute to the result
- **AND** without the filter the aggregation remains cross-instance, which the monitoring dashboard relies on

### Requirement: The standardized export SHALL be OTLP and `/metrics` SHALL NOT be promised

Traces and metrics leave the data plane through the OTLP exporter; the control-plane dashboard is fed by the heartbeat. The gateway configuration key for a Prometheus-style metrics endpoint remains present for configuration compatibility but is not implemented, and no Prometheus dependency SHALL be introduced. Documentation and code comments MUST describe it as unimplemented rather than as pending work.

#### Scenario: No listener claims the metrics endpoint

- **WHEN** the gateway configuration declares a monitoring metrics endpoint path
- **THEN** no route serves it
- **AND** the configuration model states that the key is not honoured
