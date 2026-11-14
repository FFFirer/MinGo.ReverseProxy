## ADDED Requirements

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

This requirement covers retention only. The queue has no reader yet: the gRPC heartbeat does not carry traces, so the control plane's `GET /api/telemetry/traces` reads a different, empty store instance. External visibility of these traces is delivered by the OTLP exporter, and in-process or cross-process queryability is a follow-up capability, not something this specification claims.

#### Scenario: Forwarded request leaves a local trace record

- **WHEN** a request completes with the gateway OTel pipeline registered
- **THEN** `TelemetryStore.Traces` on the data plane contains that request's gateway activity with its route, cluster, destination and status attributes

#### Scenario: Trace buffer stays bounded

- **WHEN** more activities finish than the configured trace limit
- **THEN** the oldest activities are evicted and the stored count stays at or below the limit

#### Scenario: Cross-process trace query is not claimed

- **WHEN** the control plane's trace endpoint is called after data plane requests were proxied
- **THEN** it is expected to return no entries until the heartbeat protocol carries traces
- **AND** tests and docs MUST NOT describe the trace path as end-to-end before that

### Requirement: Access logs SHALL carry a correlation key to the trace

Every access log entry produced by the gateway telemetry middleware SHALL carry a correlation identifier: the W3C trace id of the current activity when an activity exists, otherwise the ASP.NET Core request `TraceIdentifier`. The span id SHALL only be written when an activity exists. The identifier SHALL be stored on the in-memory access log model; transporting it to the control plane is out of scope for this capability.

#### Scenario: Activity present

- **WHEN** a request is recorded while a gateway activity is running
- **THEN** the access log entry carries that activity's trace id and span id
- **AND** following the identifier locates exactly that request's spans

#### Scenario: No listener, no activity

- **WHEN** no OpenTelemetry listener is registered so `StartActivity` returns null
- **THEN** the access log entry still carries the request `TraceIdentifier`
- **AND** the identifier is unique per request
- **AND** no span id is fabricated from the request identifier
