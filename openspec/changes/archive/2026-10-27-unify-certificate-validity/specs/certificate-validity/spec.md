## ADDED Requirements

### Requirement: Certificate validity SHALL be expressed as three distinct terms

The system SHALL distinguish three predicates instead of calling all of them "valid", and each SHALL have exactly one place where it decides behaviour: **loadable** (certificate bytes parse into a certificate) gates delivery and cache insertion; **domain-matched** (the SNI value equals a cached key or is covered by a cached `*.` key) decides which certificate a handshake gets; **within validity window** (`NotBefore <= now <= NotAfter`) only informs operators. No component SHALL introduce a fourth interpretation of "valid".

#### Scenario: A certificate that is expired but loadable remains selectable

- **WHEN** a certificate is loadable, domain-matched, and past its expiry
- **THEN** the data plane SHALL select it for the handshake and the only consequence of the expiry SHALL be a warning

#### Scenario: A certificate that is not loadable never reaches the data plane

- **WHEN** a stored row has no certificate bytes
- **THEN** neither the ConfigSnapshot nor the data plane cache SHALL contain it

### Requirement: Control plane SHALL compute `IsValid` server-side and treat it as historical metadata

The `IsValid` field SHALL be computed by the control plane from the certificate bytes it received, never taken from a client-supplied value, and SHALL mean "the certificate was inside its validity window at the moment it was stored". No runtime path SHALL treat it as a statement about the present.

#### Scenario: Upload computes the flag

- **WHEN** a certificate is submitted via `POST /api/certificates/upload`
- **THEN** the control plane SHALL store `IsValid` derived from the parsed certificate's validity window at that instant

#### Scenario: Client cannot set the flag

- **WHEN** a client posts a `CertificateConfig` to `POST /api/certificates` with `isValid` set to `true` for a certificate that is expired
- **THEN** the control plane SHALL overwrite the supplied value with its own computation rather than persisting the client's claim

#### Scenario: The flag is never reinterpreted as current state

- **WHEN** a stored certificate passes its expiry
- **THEN** `IsValid` SHALL keep its stored value and no filter or selection decision SHALL change because of it

### Requirement: Certificate listings SHALL derive displayed expiry state at read time

When the control plane returns certificates to callers, the expiry state it reports SHALL be computed from `ExpiresAt` at request time, so that an expired certificate is presented as expired even when its stored `IsValid` says otherwise.

#### Scenario: Expired certificate is shown as expired

- **WHEN** a stored certificate has `IsValid` true and an `ExpiresAt` in the past, and `GET /api/certificates` is called
- **THEN** the response SHALL make the expired state visible rather than reporting the stale `IsValid`

#### Scenario: Presentation does not change what is delivered

- **WHEN** a certificate is reported as expired in a listing
- **THEN** the ConfigSnapshot broadcast SHALL still carry it, because display state and delivery are separate concerns
