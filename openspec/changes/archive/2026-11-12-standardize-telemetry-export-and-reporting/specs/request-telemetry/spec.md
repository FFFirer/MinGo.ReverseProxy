## MODIFIED Requirements

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
