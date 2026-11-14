# request-telemetry Specification

## Purpose

定义数据面对外请求的遥测采集契约：每个请求的记录完整性、指标语义、标签基数边界、代理维度（cluster / destination）的来源、遥测存储的容量上界，以及指标必须同时经 OpenTelemetry instruments 导出、延迟分布必须给出分位数。

采集实现位于 `src/MinGo.DataPlane/Telemetry/GatewayTelemetryMiddleware.cs` 与 `GatewayMetrics.cs`，
OTel 装配单点位于 `src/MinGo.DataPlane/Telemetry/GatewayOpenTelemetry.cs`，
存储实现位于 `src/MinGo.Core/Services/TelemetryStore.cs`，
分位算法位于 `src/MinGo.Core/Services/MetricPercentiles.cs`。
链路侧的采集契约见 `request-tracing` 规范。

## Requirements

### Requirement: Every request SHALL be recorded exactly once in totals and access logs

The gateway telemetry middleware SHALL record exactly one `gateway.requests.total` metric point and exactly one access log entry for every request it passes to the downstream pipeline, including requests that terminate with an unhandled exception. The error rate derived from these metrics MUST NOT exceed 100%.

#### Scenario: Successful proxied request

- **WHEN** a request is proxied and completes with status 200
- **THEN** one `gateway.requests.total` point is recorded with `status=200`
- **AND** one access log entry is recorded

#### Scenario: Request terminating with an unhandled exception

- **WHEN** the downstream pipeline throws an exception that the middleware re-throws
- **THEN** one `gateway.requests.total` point is recorded
- **AND** one `gateway.requests.errors` point is recorded
- **AND** one access log entry is recorded with status 500
- **AND** the recorded duration covers the time until the exception escaped

#### Scenario: Requests are not double counted

- **WHEN** N requests pass through the middleware, of which M throw exceptions
- **THEN** `gateway.requests.total` contains exactly N points and `gateway.requests.errors` exactly M points

### Requirement: gateway.requests.errors SHALL represent gateway-side failures only

`gateway.requests.errors` SHALL be recorded only for responses with status code 500 or above and for unhandled pipeline exceptions. Client-side 4xx responses MUST NOT be counted as gateway errors, and SHALL remain observable through the `status` tag on `gateway.requests.total`.

#### Scenario: Client error is not a gateway error

- **WHEN** a request completes with status 404
- **THEN** `gateway.requests.total` records the point with `status=404`
- **AND** `gateway.requests.errors` is not incremented

#### Scenario: Gateway failure is an error

- **WHEN** a request completes with 502 because the upstream is unreachable
- **THEN** `gateway.requests.errors` is incremented with the same route, cluster and destination tags as the total

### Requirement: Metric tag values SHALL have bounded cardinality

Every metric tag value produced by the gateway telemetry middleware SHALL come from a bounded set. Requests that do not match any routing endpoint SHALL use the fixed route value `unmatched` instead of a path-derived value, while the full path and query string SHALL still be preserved on the access log entry. Exception-derived tags SHALL use the exception type name and MUST NOT contain the exception message.

#### Scenario: Unmatched request does not create a new series

- **WHEN** requests arrive for many distinct paths that match no endpoint (for example `/scan/aaa`, `/scan/bbb`)
- **THEN** each records `gateway.requests.total` with `route=unmatched`
- **AND** no additional distinct `route` tag value is created
- **AND** each access log entry still carries the full request path

#### Scenario: Matched proxy route uses the route identifier

- **WHEN** a request matches YARP route `r1`
- **THEN** the metric tag is `route=r1`

#### Scenario: Exception tag is the type name

- **WHEN** the pipeline throws `InvalidOperationException`
- **THEN** the errors metric carries `error=InvalidOperationException`
- **AND** the exception message is not present in any metric tag

### Requirement: Proxied requests SHALL carry cluster and destination tags

For requests handled by the reverse proxy, the middleware SHALL read `Yarp.ReverseProxy.Model.IReverseProxyFeature` after the downstream pipeline completes and SHALL tag the metrics with `cluster` from `Cluster?.Config?.ClusterId` and `destination` from `ProxiedDestination?.DestinationId`. When the feature is absent, or when no destination was selected, the middleware SHALL write the fixed sentinel value `none` for the unknown dimension rather than omitting the tag or storing null.

#### Scenario: Proxied request exposes the selected destination

- **WHEN** a request matching route `r1` is forwarded to destination `d1` of cluster `backend`
- **THEN** `gateway.requests.total` is recorded with `route=r1`, `cluster=backend`, `destination=d1`

#### Scenario: No destination selected

- **WHEN** a proxied request fails without a destination being selected
- **THEN** the metric is recorded with the resolved cluster and `destination=none`

#### Scenario: Request bypasses the reverse proxy

- **WHEN** a request is served by an endpoint other than the reverse proxy
- **THEN** the metric is recorded with `cluster=none` and `destination=none`

### Requirement: TelemetryStore SHALL bound stored metric points

`TelemetryStore` SHALL cap the number of retained metric points per metric name, with a default limit of 50000 points and a configurable override via `Telemetry:MaxMetricPoints`. Eviction SHALL remove the oldest points first. The count of evicted points SHALL be exposed as the `gateway.telemetry.dropped` metric with a fixed, empty tag set, and its point value SHALL be the cumulative count rather than a per-write delta.

The access-log buffer SHALL be bounded the same way (default 5000 entries, oldest discarded first) and its discards SHALL be counted, so that entries lost while the heartbeat drains at most one page are visible instead of silent. Bounding decisions on the request path MUST NOT be made using `ConcurrentQueue.Count`, which is O(n); both bounds SHALL be enforced with bookkeeping counters maintained by the store.

#### Scenario: Cap prevents unbounded growth

- **WHEN** more than the configured maximum number of points are recorded for one metric name
- **THEN** the oldest points are evicted and the stored count stays at or below the limit
- **AND** `gateway.telemetry.dropped` reflects the cumulative number of evicted points

#### Scenario: Retention window shrinks under load rather than memory growing

- **WHEN** sustained traffic exceeds the point cap within the 24-hour window
- **THEN** stored points are evicted before the 24-hour cutoff applies
- **AND** the eviction counter makes the reduced effective retention observable instead of silent

#### Scenario: The self-observing metric does not recurse

- **WHEN** recording `gateway.telemetry.dropped` would itself exceed the point cap
- **THEN** its points are trimmed without producing further dropped-metric samples

#### Scenario: Access log overflow is bounded and counted

- **WHEN** access logs are appended faster than they are drained and the buffer reaches its bound
- **THEN** the oldest entries are discarded and the stored count stays at or below the limit
- **AND** the discard count increases by exactly the number of discarded entries

#### Scenario: Health checks are excluded

- **WHEN** a request targets a path starting with `/healthz`
- **THEN** no gateway metric point and no access log entry is recorded

### Requirement: Gateway metrics SHALL be emitted through OpenTelemetry instruments

The data plane SHALL record gateway request metrics through OpenTelemetry instruments created on the `Gateway` Meter, so that the configured `AddMeter("Gateway")` subscription produces gateway metrics on the OTLP export path. `gateway.requests.total` and `gateway.requests.errors` SHALL be counters and `gateway.request.duration` SHALL be a histogram. The instrument tag set SHALL be identical to the tag set written to `TelemetryStore`, and SHALL remain bounded as required by the metric tag cardinality requirement.

#### Scenario: Gateway meter produces the three instruments

- **WHEN** requests pass through the telemetry middleware with the gateway OTel pipeline registered
- **THEN** the `Gateway` meter has `gateway.requests.total`, `gateway.requests.errors` and `gateway.request.duration` instruments
- **AND** the measurement carries the `route`, `cluster`, `destination` and `status` dimensions

#### Scenario: Internal store and instruments stay consistent

- **WHEN** a request is recorded
- **THEN** the instrument measurement and the `TelemetryStore` metric point are produced from the same tag construction site
- **AND** the metric names written to the store are the instrument names, so a rename cannot silently split the two export paths

#### Scenario: Disabling OpenTelemetry keeps internal telemetry working

- **WHEN** `OpenTelemetry:Enabled` is `false`
- **THEN** `TelemetryStore` still receives the metric points and access log entries
- **AND** no request fails because the OTLP pipeline is absent

### Requirement: The metrics summary SHALL expose latency percentiles

`MetricsSummary` SHALL report P50, P95 and P99 request latency in addition to the average, computed with the same nearest-rank method used for `RequestMetrics` (`MinGo.Core` `MetricPercentiles.NearestRank`). Percentiles are computed over the retained sample window rather than the full history, and are an approximation when the window is capped — they MUST NOT be presented as exact histogram quantiles.

#### Scenario: Summary reports tail latency

- **WHEN** the monitoring summary is requested and latency samples exist
- **THEN** `P50ResponseTime`, `P95ResponseTime` and `P99ResponseTime` are populated from the retained duration samples
- **AND** `P99` is greater than or equal to `P50` for the same sample set

#### Scenario: No samples

- **WHEN** no latency samples are retained
- **THEN** all percentile fields are zero rather than null or undefined
