## MODIFIED Requirements

### Requirement: Data plane SHALL expose HTTPS endpoint for TLS termination

The data plane SHALL expose an HTTPS endpoint that terminates TLS connections from clients. The data plane SHALL use the SNI (Server Name Indication) value from the TLS handshake to select the appropriate certificate for the requested domain. When the client supplies an SNI value that matches no configured certificate, the data plane SHALL NOT present a certificate for a different domain and the handshake SHALL fail. The fallback certificate SHALL be used only when the client supplies no SNI value at all. The data plane SHALL support TLS 1.2 and TLS 1.3 protocols. The data plane MUST NOT require a restart when certificates are updated.

#### Scenario: Data plane listens on HTTPS port

- **WHEN** the data plane starts
- **THEN** it SHALL listen on port 8443 for HTTPS connections in addition to port 8080 for HTTP

#### Scenario: TLS handshake uses SNI to select certificate

- **WHEN** a client initiates a TLS handshake with an SNI value of `api.example.com`
- **THEN** the data plane SHALL select the certificate configured for `api.example.com` and complete the TLS handshake

#### Scenario: No matching certificate fails the handshake

- **WHEN** a client initiates a TLS handshake with an SNI value that does not match any configured certificate
- **THEN** the data plane SHALL NOT present any certificate and the TLS handshake SHALL fail

#### Scenario: Non-SNI client connection

- **WHEN** a client initiates a TLS handshake without an SNI value
- **THEN** the data plane SHALL use the fallback certificate to complete the handshake

### Requirement: Data plane SHALL select certificate by domain with wildcard support

The data plane SHALL maintain a mapping of domain names to X509 certificates. When selecting a certificate for a TLS handshake, it SHALL first attempt exact domain match, then wildcard domain match (e.g., `*.example.com` matching `sub.example.com`). If neither matches, it SHALL return no certificate rather than falling back, because presenting a certificate whose name does not match the SNI the client asked for is not a usable state. The fallback certificate SHALL be reached only when the SNI value is absent. Domain matching SHALL be case-insensitive.

#### Scenario: Exact domain match selects correct certificate

- **WHEN** the data plane receives an SNI value of `admin.example.com` and a certificate exists for `admin.example.com`
- **THEN** the data plane SHALL select the `admin.example.com` certificate

#### Scenario: Wildcard domain matches subdomain

- **WHEN** the data plane receives an SNI value of `app.example.com` and a certificate exists for `*.example.com`
- **THEN** the data plane SHALL select the `*.example.com` certificate

#### Scenario: Wildcard does NOT match apex domain

- **WHEN** the data plane receives an SNI value of `example.com` and only a certificate for `*.example.com` exists
- **THEN** the data plane SHALL NOT match the wildcard certificate and SHALL NOT present any other certificate; the TLS handshake SHALL fail

#### Scenario: Unmatched SNI is logged at warning level

- **WHEN** a handshake fails because the supplied SNI matches no cached certificate
- **THEN** the data plane SHALL log a warning naming the requested SNI, because Kestrel itself reports this failure only at debug level and closes the connection without a TLS alert

### Requirement: Data plane SHALL reload certificates on config update without restart

When the data plane receives new certificate data from the control plane via the ConfigSnapshot, it SHALL update its in-memory certificate cache (load new X509Certificate2 objects from bytes, dispose old ones) and make them available for subsequent TLS handshakes without requiring a process restart. Loading SHALL be gated only on whether the bytes can be parsed into a certificate; the data plane SHALL NOT filter entries by the stored `is_valid` flag or by expiry, because an expired certificate must remain addressable by its domain (see `certificate-validity`).

#### Scenario: New certificate used for subsequent TLS handshakes

- **WHEN** the data plane receives a ConfigSnapshot containing a new certificate for `api.example.com`
- **THEN** the next TLS handshake with SNI `api.example.com` SHALL use the new certificate

#### Scenario: Removed certificate no longer served

- **WHEN** the data plane receives a ConfigSnapshot that does not include a previously loaded certificate
- **THEN** subsequent TLS handshakes with the corresponding SNI value SHALL NOT present any certificate and SHALL fail

## ADDED Requirements

### Requirement: Fallback certificate SHALL be deterministic and reserved for connections without SNI

The certificate used when a client supplies no SNI value SHALL be selected by a fixed rule rather than by load order: a certificate cached under the exact key `localhost` takes precedence, otherwise the certificate with the lexicographically smallest domain key (wildcard keys included) is used. The data plane SHALL log which domain and thumbprint it chose as the fallback certificate when the cache is rebuilt.

#### Scenario: Localhost certificate wins the fallback slot

- **WHEN** the certificate cache contains entries for `api.example.com` and `localhost`
- **THEN** a handshake without an SNI value SHALL use the `localhost` certificate

#### Scenario: Smallest domain key wins when no localhost certificate exists

- **WHEN** the certificate cache contains entries for `zeta.example.com` and `alpha.example.com` and no `localhost` entry
- **THEN** a handshake without an SNI value SHALL use the `alpha.example.com` certificate

#### Scenario: Fallback choice is stable across restarts

- **WHEN** the same certificate set is reloaded after a process restart
- **THEN** the fallback certificate SHALL be the same certificate as before the restart

### Requirement: Data plane SHALL serve expired certificates and log a warning

When a certificate in the ConfigSnapshot has an `expires_at` value in the past, the data plane SHALL still load it into the cache and SHALL serve it for handshakes matching its domain, recording a warning that names the domain and the expiry time. The data plane SHALL NOT reject an expired certificate at the handshake layer; expiry is enforced by the client, and a readable certificate error is operationally preferable to a bare handshake failure.

#### Scenario: Expired certificate is loaded with a warning

- **WHEN** the data plane receives a ConfigSnapshot containing a certificate for `api.example.com` whose `expires_at` is in the past
- **THEN** the certificate SHALL be present in the cache and a warning naming `api.example.com` SHALL be logged

#### Scenario: Expired certificate completes the handshake

- **WHEN** a client initiates a TLS handshake with SNI `api.example.com` and the only certificate for that domain is expired
- **THEN** the data plane SHALL present that expired certificate and the client SHALL be the party that rejects it

#### Scenario: Expiry never removes a domain from the cache

- **WHEN** a certificate expires while the data plane keeps running with no new ConfigSnapshot
- **THEN** the certificate SHALL remain addressable by its domain name
