## MODIFIED Requirements

### Requirement: Broadcast ConfigSnapshot SHALL include certificate data

When the control plane broadcasts a ConfigSnapshot to connected data planes (either on initial connection or on config change), it SHALL include exactly one certificate per domain name (compared case-insensitively), carrying the domain name, certificate bytes, password, thumbprint, stored `is_valid` value and `expires_at`. The set SHALL be limited to certificates that actually carry non-empty certificate bytes, so that no entry is delivered that the data plane cannot load. The set SHALL NOT be filtered by the stored `is_valid` flag or by expiry: an expired certificate MUST keep its domain addressable in the data plane cache, which is a precondition for `https-redirect`'s fail-closed behaviour. Domains with several certificates SHALL be resolved to the one expiring last among those not yet expired, or to the one expiring last overall when every certificate for the domain is expired. The resulting certificate list SHALL be emitted in a deterministic order.

#### Scenario: Initial config snapshot includes all loadable certificates

- **WHEN** a data plane connects and subscribes to config updates
- **THEN** the control plane SHALL send a ConfigSnapshot that includes all routes, clusters, AND one entry per domain for every certificate with non-empty certificate bytes

#### Scenario: Config change broadcast includes all certificates

- **WHEN** a route or cluster change triggers a ConfigSnapshot broadcast
- **THEN** the broadcast SHALL include the same certificate set as during initial connection

#### Scenario: Certificate without bytes is not delivered

- **WHEN** a stored certificate row has `CertificateData` null or empty
- **THEN** the ConfigSnapshot SHALL NOT contain an entry for it and the data plane SHALL NOT receive a dead entry

#### Scenario: Expired certificate is still delivered

- **WHEN** a certificate's expiry time is in the past
- **THEN** the ConfigSnapshot SHALL still contain its entry, so that the data plane keeps matching that domain

#### Scenario: Repeated broadcasts of unchanged data are identical

- **WHEN** the same certificate rows are assembled twice without any change
- **THEN** the certificate entries SHALL appear in the same order both times

#### Scenario: Newest valid certificate wins for a duplicated domain

- **WHEN** two certificates exist for `api.example.com`, one expiring sooner and one expiring later, neither expired
- **THEN** the ConfigSnapshot SHALL contain the later-expiring one only

#### Scenario: Latest-expiring certificate wins when all are expired

- **WHEN** two certificates exist for `api.example.com` and both are expired
- **THEN** the ConfigSnapshot SHALL contain the one expiring last, so that the client-facing expiry date is the most recent one

## ADDED Requirements

### Requirement: Stored `is_valid` SHALL NOT gate config replication

The control plane SHALL continue to populate the `is_valid` field of `CertificateData` as historical metadata from the moment the certificate was stored, and SHALL NOT use it, nor any field derived from it, as a filter when assembling a ConfigSnapshot.

#### Scenario: A row marked invalid is judged only by its bytes

- **WHEN** a certificate row has `is_valid` false but non-empty certificate bytes
- **THEN** the ConfigSnapshot SHALL include it and the data plane SHALL load it like any other certificate
