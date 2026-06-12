# Cert Sync Reload

**Purpose**: Mechanism for certificate data to be pushed from control plane to data plane and trigger certificate cache reload.

## Requirements

### Requirement: Certificate CRUD operations SHALL trigger config update broadcast

When a certificate is created, updated, or deleted via the control plane REST API, the control plane SHALL publish a ConfigUpdate event. This event SHALL trigger a gRPC ConfigSnapshot broadcast to all connected data planes, ensuring the data planes receive the latest certificate data.

#### Scenario: Certificate creation triggers broadcast

- **WHEN** a user creates a new certificate via `POST /api/certificates`
- **THEN** the control plane SHALL push a ConfigSnapshot (with all current certificates) to all connected data planes

#### Scenario: Certificate update triggers broadcast

- **WHEN** a user updates an existing certificate via `PUT /api/certificates/{id}`
- **THEN** the control plane SHALL push a ConfigSnapshot (with all current certificates) to all connected data planes

#### Scenario: Certificate deletion triggers broadcast

- **WHEN** a user deletes a certificate via `DELETE /api/certificates/{id}`
- **THEN** the control plane SHALL push a ConfigSnapshot (with remaining certificates) to all connected data planes

### Requirement: Broadcast ConfigSnapshot SHALL include certificate data

When the control plane broadcasts a ConfigSnapshot to connected data planes (either on initial connection or on config change), it SHALL include the full certificate data (domain name, certificate bytes, password, thumbprint, validity) for all valid certificates. This ensures the data plane has all certificates needed for TLS termination.

#### Scenario: Initial config snapshot includes all certificates

- **WHEN** a data plane connects and subscribes to config updates
- **THEN** the control plane SHALL send a ConfigSnapshot that includes all routes, clusters, AND certificates

#### Scenario: Config change broadcast includes all certificates

- **WHEN** a route or cluster change triggers a ConfigSnapshot broadcast
- **THEN** the broadcast SHALL include all current certificates, matching the same data sent during initial connection

### Requirement: Data plane SHALL reload certificate selector cache after receiving certificates

When the data plane's ConfigSyncService receives a ConfigSnapshot with certificate data, it SHALL:
1. Update the DataPlaneConfigProvider's certificate data store
2. Call DataPlaneCertificateSelector.ReloadFromProvider() to load new X509Certificate2 objects from the received bytes
3. Dispose old certificate objects that are no longer in use

#### Scenario: Certificates reloaded after ConfigSnapshot

- **WHEN** ConfigSyncService.ApplyConfigSnapshotAsync processes a ConfigSnapshot containing certificates
- **THEN** DataPlaneCertificateSelector.ReloadFromProvider() SHALL be called immediately after UpdateCertificates()
