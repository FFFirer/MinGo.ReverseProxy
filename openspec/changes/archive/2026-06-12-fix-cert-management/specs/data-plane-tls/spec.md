## ADDED Requirements

### Requirement: Data plane SHALL expose HTTPS endpoint for TLS termination

The data plane SHALL expose an HTTPS endpoint that terminates TLS connections from clients. The data plane SHALL use the SNI (Server Name Indication) value from the TLS handshake to select the appropriate certificate for the requested domain. The data plane SHALL support TLS 1.2 and TLS 1.3 protocols. The data plane MUST NOT require a restart when certificates are updated.

#### Scenario: Data plane listens on HTTPS port

- **WHEN** the data plane starts
- **THEN** it SHALL listen on port 8443 for HTTPS connections in addition to port 8080 for HTTP

#### Scenario: TLS handshake uses SNI to select certificate

- **WHEN** a client initiates a TLS handshake with an SNI value of `api.example.com`
- **THEN** the data plane SHALL select the certificate configured for `api.example.com` and complete the TLS handshake

#### Scenario: No matching certificate returns fallback

- **WHEN** a client initiates a TLS handshake with an SNI value that does not match any configured certificate
- **THEN** the data plane SHALL use the default/fallback certificate to complete the handshake

#### Scenario: Non-SNI client connection

- **WHEN** a client initiates a TLS handshake without an SNI value
- **THEN** the data plane SHALL use the default/fallback certificate

### Requirement: Data plane SHALL select certificate by domain with wildcard support

The data plane SHALL maintain a mapping of domain names to X509 certificates. When selecting a certificate for a TLS handshake, it SHALL first attempt exact domain match, then wildcard domain match (e.g., `*.example.com` matching `sub.example.com`), and finally fall back to the default certificate. Domain matching SHALL be case-insensitive.

#### Scenario: Exact domain match selects correct certificate

- **WHEN** the data plane receives an SNI value of `admin.example.com` and a certificate exists for `admin.example.com`
- **THEN** the data plane SHALL select the `admin.example.com` certificate

#### Scenario: Wildcard domain matches subdomain

- **WHEN** the data plane receives an SNI value of `app.example.com` and a certificate exists for `*.example.com`
- **THEN** the data plane SHALL select the `*.example.com` certificate

#### Scenario: Wildcard does NOT match apex domain

- **WHEN** the data plane receives an SNI value of `example.com` and only a certificate for `*.example.com` exists
- **THEN** the data plane SHALL NOT match the wildcard certificate and SHALL fall back to the default certificate

### Requirement: Data plane SHALL reload certificates on config update without restart

When the data plane receives new certificate data from the control plane via the ConfigSnapshot, it SHALL update its in-memory certificate cache (load new X509Certificate2 objects from bytes, dispose old ones) and make them available for subsequent TLS handshakes without requiring a process restart.

#### Scenario: New certificate used for subsequent TLS handshakes

- **WHEN** the data plane receives a ConfigSnapshot containing a new certificate for `api.example.com`
- **THEN** the next TLS handshake with SNI `api.example.com` SHALL use the new certificate

#### Scenario: Removed certificate no longer served

- **WHEN** the data plane receives a ConfigSnapshot that does not include a previously loaded certificate
- **THEN** subsequent TLS handshakes with the corresponding SNI value SHALL fall back to the default certificate

### Requirement: Development environment SHALL support self-signed certificates

In development environment, when no certificate is configured for a domain, the data plane SHALL use a self-signed development certificate to allow TLS testing without real certificates.

#### Scenario: Development certificate auto-creation

- **WHEN** the data plane runs in development mode and no certificates are configured
- **THEN** a self-signed certificate for `localhost` SHALL be generated and used as the default certificate
