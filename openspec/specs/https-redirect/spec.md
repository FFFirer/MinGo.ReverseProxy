# https-redirect Specification

## Purpose
TBD - created by archiving change enable-https-redirect. Update Purpose after archive.
## Requirements
### Requirement: HttpsRedirect SHALL be enabled by default with an explicit public port

The data plane SHALL enable HTTP-to-HTTPS redirection by default through the `HttpsRedirect` configuration section, which SHALL provide `Enabled` (default `true`), `Port` (default `8443`, the publicly reachable HTTPS port used to build the `Location` header), and `HttpOnlyHosts` (default empty). `Port` SHALL be independent of the Kestrel listening ports: `Kestrel:Endpoints` and `ASPNETCORE_URLS` decide which sockets are bound, `HttpsRedirect:Port` decides only what value appears in the redirect target. The `Development` environment SHALL default `Enabled` to `false` so local verification keeps plain-HTTP access working.

#### Scenario: Default configuration enables redirection

- **WHEN** the data plane starts with `appsettings.json` and no override
- **THEN** `HttpsRedirect:Enabled` SHALL be `true` and `HttpsRedirect:Port` SHALL be `8443`

#### Scenario: Development environment disables redirection

- **WHEN** the data plane runs with `ASPNETCORE_ENVIRONMENT=Development`
- **THEN** no request SHALL be redirected and plain HTTP proxying behaves exactly as before this change

#### Scenario: Local compose path is also disabled

- **WHEN** `docker-compose.local.yml` starts the data plane (which pins `ASPNETCORE_ENVIRONMENT=Production`)
- **THEN** `HttpsRedirect__Enabled=false` SHALL be set so `http://localhost:8080` verification assertions keep passing

### Requirement: Redirection SHALL apply only when the request host matches a certificate without default fallback

A plain-HTTP request SHALL be redirected only if its host (case-insensitive, port stripped) matches a certificate in the data plane's certificate cache by exact domain or by `*.` wildcard suffix. This check SHALL NOT fall back to the default certificate — unlike `SelectCertificate`, which returns it for SNI-less handshakes. Hosts that match no certificate SHALL continue to be served over plain HTTP — this is how a domain opts out of HTTPS.

#### Scenario: Host with its own certificate is redirected

- **WHEN** `api.example.com` exists in the certificate cache and a plain-HTTP request arrives for it
- **THEN** the data plane SHALL respond `308 Permanent Redirect` with `Location: https://api.example.com:8443{path}{query}`

#### Scenario: Wildcard certificate covers a subdomain

- **WHEN** only `*.example.com` is in the cache and a plain-HTTP request arrives for `shop.example.com`
- **THEN** the request SHALL be redirected

#### Scenario: Default certificate does not authorize redirection

- **WHEN** only `other.example.net` is in the cache (and it became the default certificate) and a plain-HTTP request arrives for `api.example.com`
- **THEN** the request SHALL NOT be redirected and SHALL be proxied over plain HTTP

#### Scenario: Expired certificate still triggers redirection

- **WHEN** a certificate for `api.example.com` is present in the cache but already expired
- **THEN** the request SHALL still be redirected — no plain-HTTP fallback is introduced by expiry
- **AND** the resulting TLS handshake failure is expected behavior, not a redirection defect

### Requirement: Redirected requests SHALL preserve method, path and query with status 308

The data plane SHALL use status code `308 Permanent Redirect` for all HTTPS redirection. The redirect target SHALL preserve the request path and query string unchanged, SHALL keep the request host (with any incoming port replaced by `HttpsRedirect:Port`), and SHALL NOT replace the built-in middleware's URI construction with a custom implementation.

#### Scenario: Incoming host carries the plain-HTTP port

- **WHEN** a request arrives with `Host: api.example.com:8080` and `HttpsRedirect:Port` is `8443`
- **THEN** `Location` SHALL be `https://api.example.com:8443{path}{query}` with no port duplication

#### Scenario: POST body survives the redirect

- **WHEN** a `POST` request with a body to a certificate-backed host is redirected
- **THEN** the response SHALL be `308` and a client following the redirect SHALL re-issue `POST` with the same body

### Requirement: Health endpoints and unusable hosts SHALL never be redirected

The data plane SHALL NOT redirect: requests whose path starts with `/healthz`; requests with an empty or missing `Host` header; requests addressed to `localhost` or to an IP literal. This prevents malformed targets such as `Location: https://:8443/ping`, keeps container healthchecks working, and leaves internal probe and monitoring traffic on plain HTTP.

#### Scenario: Health endpoints are reachable over plain HTTP

- **WHEN** `GET http://<host>:8080/healthz/live` or `/healthz/ready` is requested
- **THEN** the data plane SHALL return `200` without any redirect

#### Scenario: Missing Host header does not produce a malformed target

- **WHEN** an `HTTP/1.0` request without a `Host` header arrives
- **THEN** the data plane SHALL NOT emit a `Location` containing an empty host

#### Scenario: IP literal access stays plain

- **WHEN** a plain-HTTP request uses `Host: 192.168.1.10:8080`
- **THEN** the request SHALL NOT be redirected

### Requirement: Configuration-hosted plain-HTTP exceptions SHALL override certificate presence

The `HttpsRedirect:HttpOnlyHosts` list SHALL suppress redirection for the matched hosts even when a certificate exists for them. Matching SHALL support exact domains and `*.` wildcards, case-insensitively.

#### Scenario: Listed host is served over plain HTTP despite having a certificate

- **WHEN** `monitor.internal.example.com` has a certificate AND appears in `HttpOnlyHosts`
- **THEN** plain-HTTP requests for it SHALL be proxied, not redirected

### Requirement: The data plane SHALL refuse to start when redirection is enabled without a valid port

The built-in redirection middleware fails open: when it cannot resolve an HTTPS port it logs a warning and serves the request over plain HTTP. The data plane SHALL NOT inherit that behavior — with `Enabled = true` and `Port` missing or outside `1-65535`, startup SHALL throw and the process SHALL NOT serve traffic.

#### Scenario: Enabled without a valid port fails at startup

- **WHEN** the data plane starts with `HttpsRedirect:Enabled = true` and `HttpsRedirect:Port` unset or set to `0`
- **THEN** startup SHALL throw `InvalidOperationException` and the process SHALL exit non-zero

#### Scenario: TLS handshake behavior is unchanged

- **WHEN** any of the above redirection rules are active
- **THEN** `SelectCertificate` SHALL keep its existing exact → wildcard → default-certificate fallback, and the `data-plane-tls` requirements SHALL remain satisfied without modification

