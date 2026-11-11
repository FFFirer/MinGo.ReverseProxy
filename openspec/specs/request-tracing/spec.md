# request-tracing Specification

## Purpose

定义数据面对外请求的链路采集与关联契约：每请求一条网关 server span、YARP 转发 span 必须进入链路、Activity 结束后在数据面本地有界保留、访问日志携带可回查链路的关联键，以及这些能力在未启用 OpenTelemetry 时的下限行为。

采集实现位于 `src/MinGo.DataPlane/Telemetry/GatewayTelemetryMiddleware.cs`，
OTel 装配单点（source 名、本地 processor、OTLP 出口）位于 `src/MinGo.DataPlane/Telemetry/GatewayOpenTelemetry.cs`，
本地落地实现为 `LocalTraceExporter.cs`，存储为 `src/MinGo.Core/Services/TelemetryStore.cs`。
指标与访问日志本身的语义见 `request-telemetry` 规范。

## Requirements

### Requirement: The gateway SHALL produce one server span per request

The data plane SHALL create exactly one `Gateway` ActivitySource activity with `ActivityKind.Server` per recorded request, SHALL set the resolved `route`, `cluster`, `destination`, `status` and duration attributes on it, and SHALL mark it as an error when the request failed with a gateway-side error — the same condition that increments `gateway.requests.errors`, namely an unhandled pipeline exception or a response status of 500 or above.

#### Scenario: Successful request span

- **WHEN** a request is proxied successfully
- **THEN** a `Gateway` activity is produced containing `route`, `cluster`, `destination`, `status` and duration attributes
- **AND** its status is not error

#### Scenario: Failed request span

- **WHEN** a request ends with a gateway-side error, including an upstream 5xx returned without an exception
- **THEN** the activity status is set to error

### Requirement: YARP forwarder spans SHALL be included in the trace

The tracing pipeline SHALL listen to the `Yarp.ReverseProxy` activity source in addition to `Gateway`, so the forwarder span carrying `proxy.route_id`, `proxy.cluster_id` and `proxy.destination_id` is retained and exported in the same trace as the gateway span.

#### Scenario: Forwarder attempt is visible

- **WHEN** a request is forwarded to an upstream destination with the gateway OTel pipeline registered
- **THEN** a `Yarp.ReverseProxy` `proxy.forwarder` activity is produced for the same trace
- **AND** it is not dropped by source filtering

#### Scenario: Source name is exact

- **WHEN** the tracing configuration registers sources
- **THEN** the registered YARP source name is exactly `Yarp.ReverseProxy`
- **AND** a wrong spelling fails the end-to-end trace assertion rather than silently producing no data

### Requirement: Finished activities SHALL be retained locally

The data plane SHALL register a local exporter that appends every finished activity it samples to the data plane's own `TelemetryStore.Traces`, bounded to the existing trace limit. Trace retention bookkeeping on the request path MUST NOT use `ConcurrentQueue.Count`, which is O(n).

This requirement covers retention only. Reporting activity samples to the control plane is a settled non-goal, not pending work: the heartbeat carries no trace payload, so the control plane's `GET /api/telemetry/traces` reads its own, empty store instance. External visibility of traces is delivered by the OTLP exporter, and cross-process correlation is delivered by the access-log correlation key rather than by shipping spans.

#### Scenario: Forwarded request leaves a local trace record

- **WHEN** a request completes with the gateway OTel pipeline registered
- **THEN** `TelemetryStore.Traces` on the data plane contains that request's gateway activity with its route, cluster, destination and status attributes

#### Scenario: Trace buffer stays bounded

- **WHEN** more activities finish than the configured trace limit
- **THEN** the oldest activities are evicted and the stored count stays at or below the limit

#### Scenario: Cross-process trace query is not claimed

- **WHEN** the control plane's trace endpoint is called after data plane requests were proxied
- **THEN** it returns no entries, because spans are intentionally not transported
- **AND** tests, docs and specs MUST NOT describe the control-plane trace path as end-to-end
- **AND** they MUST NOT describe that as the reason a log entry cannot be correlated — the correlation key does cross the boundary

### Requirement: Access logs SHALL carry a correlation key to the trace

Every access log entry produced by the gateway telemetry middleware SHALL carry a correlation identifier: the W3C trace id of the current activity when an activity exists, otherwise the ASP.NET Core request `TraceIdentifier`. The span id SHALL only be written when an activity exists. The identifier SHALL be stored on the in-memory access log model **and SHALL reach the control plane through the heartbeat**, where it is served by the access-log query surface; the transport contract itself is specified in `telemetry-reporting`.

#### Scenario: Activity present

- **WHEN** a request is recorded while a gateway activity is running
- **THEN** the access log entry carries that activity's trace id and span id
- **AND** following the identifier locates exactly that request's spans

#### Scenario: No listener, no activity

- **WHEN** no OpenTelemetry listener is registered so `StartActivity` returns null
- **THEN** the access log entry still carries the request `TraceIdentifier`
- **AND** the identifier is unique per request
- **AND** no span id is fabricated from the request identifier

#### Scenario: The key survives the process boundary

- **WHEN** an access log entry with a trace id is reported to the control plane
- **THEN** the entry the control plane serves exposes that same trace id
- **AND** the correlation established in the data plane is not lost at the reporting hop
