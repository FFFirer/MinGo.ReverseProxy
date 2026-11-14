## MODIFIED Requirements

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
