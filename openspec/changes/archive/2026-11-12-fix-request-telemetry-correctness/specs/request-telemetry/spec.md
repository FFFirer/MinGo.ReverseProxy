## ADDED Requirements

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

- **WHEN** a request completes with status 502 because the upstream is unreachable
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

- **WHEN** a request matching route `r1` is forwarded to destination `backend-1` of cluster `backend`
- **THEN** `gateway.requests.total` is recorded with `route=r1`, `cluster=backend`, `destination=backend-1`

#### Scenario: All destinations unavailable

- **WHEN** a proxied request fails with 502 because no destination was selected
- **THEN** the metric is recorded with `cluster=backend` and `destination=none`

#### Scenario: Request bypasses the reverse proxy

- **WHEN** a request is served by an endpoint other than the reverse proxy
- **THEN** the metric is recorded with `cluster=none` and `destination=none`

### Requirement: TelemetryStore SHALL bound stored metric points

`TelemetryStore` SHALL cap the number of retained metric points per metric name, with a default limit of 50000 points and a configurable override. Eviction SHALL remove the oldest points first. The count of evicted points SHALL be exposed as the `gateway.telemetry.dropped` metric with a fixed tag set. Bounding decisions on the request path MUST NOT be made using `ConcurrentQueue.Count`, which is O(n).

#### Scenario: Cap prevents unbounded growth

- **WHEN** more than the configured maximum number of points are recorded for one metric name
- **THEN** the oldest points are evicted and the stored count stays at or below the limit
- **AND** `gateway.telemetry.dropped` reflects the number of evicted points

#### Scenario: Retention window shrinks under load rather than memory growing

- **WHEN** sustained traffic exceeds the point cap within the 24-hour window
- **THEN** stored points are evicted before the 24-hour cutoff applies
- **AND** the eviction counter makes the reduced effective retention observable instead of silent

#### Scenario: Health checks are excluded

- **WHEN** a request targets a path starting with `/healthz`
- **THEN** no gateway metric point and no access log entry is recorded
