## ADDED Requirements

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
