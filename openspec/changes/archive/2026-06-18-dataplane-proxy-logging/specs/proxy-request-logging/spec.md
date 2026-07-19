## ADDED Requirements

### Requirement: Request logging enabled

The data plane SHALL log every HTTP proxy request with its metadata after the response is produced.

The log entry SHALL include:
- Request method
- Request path
- Response status code
- Elapsed time in milliseconds

All requests SHALL be logged at `Information` level by default.

#### Scenario: Successful request logged
- **WHEN** the data plane receives a GET request to `/api/users` and returns 200 in 45ms
- **THEN** the log output SHALL contain an `Information` level entry with `GET`, `/api/users`, `200`, and elapsed time ~45ms

#### Scenario: Error request logged
- **WHEN** the data plane receives a POST request and the upstream returns 500
- **THEN** the log output SHALL contain an `Information` level entry with `POST`, the request path, `500`, and elapsed time

### Requirement: Configurable log output format

The data plane SHALL support switching log output format via `appsettings.json` configuration without code changes.

#### Scenario: Default plain text output
- **WHEN** the data plane starts with default configuration
- **THEN** the console output SHALL be in Serilog default plain text format containing the HTTP request details

#### Scenario: Switch to JSON output
- **WHEN** the Console formatter is configured to `CompactJsonFormatter` in `appsettings.json`
- **THEN** the console output SHALL be in compact JSON format
