## ADDED Requirements

### Requirement: ConfigValidator SHALL validate ConfigSnapshot before push

The Control Plane SHALL validate the ConfigSnapshot before pushing it to Data Planes. The validator SHALL check all Routes, Clusters, Destinations, and Certificates in the snapshot.

#### Scenario: Valid config passes validation

- **WHEN** a ConfigSnapshot contains valid Routes, Clusters, Destinations, and Certificates
- **THEN** the validator returns ValidationResult.IsValid = true

#### Scenario: Invalid config fails validation

- **WHEN** a ConfigSnapshot contains any invalid data (e.g., Route referencing non-existent Cluster)
- **THEN** the validator returns ValidationResult.IsValid = false with error details

### Requirement: Route SHALL reference existing Cluster

Every Route in the ConfigSnapshot SHALL have a ClusterId that references an existing Cluster in the same snapshot.

#### Scenario: Route references non-existent Cluster

- **WHEN** a Route has ClusterId = "cluster-1" but no Cluster with Id "cluster-1" exists in the snapshot
- **THEN** the validator adds error with code "ROUTE_CLUSTER_NOT_FOUND" and the Route's Id as EntityId

#### Scenario: Route has empty ClusterId

- **WHEN** a Route has an empty or whitespace ClusterId
- **THEN** the validator adds error with code "ROUTE_EMPTY_CLUSTER_REF"

### Requirement: Destination Address SHALL be valid URL

Every Destination in the ConfigSnapshot SHALL have a non-empty Address that starts with "http://" or "https://".

#### Scenario: Destination has empty address

- **WHEN** a Destination has an empty or whitespace Address
- **THEN** the validator adds error with code "DEST_EMPTY_ADDRESS"

#### Scenario: Destination has invalid URL format

- **WHEN** a Destination Address does not start with "http://" or "https://"
- **THEN** the validator adds error with code "DEST_INVALID_URL"

### Requirement: LoadBalancingPolicy SHALL be valid YARP policy

Every Cluster in the ConfigSnapshot SHALL have a LoadBalancingPolicy that is one of: RoundRobin, Random, LeastRequests, PowerOfTwoChoices, LeastConnection (case-insensitive).

#### Scenario: Cluster has invalid LoadBalancingPolicy

- **WHEN** a Cluster has LoadBalancingPolicy = "InvalidPolicy"
- **THEN** the validator adds error with code "CLUSTER_INVALID_LB_POLICY"

### Requirement: Cluster SHALL have at least one Destination

Every Cluster in the ConfigSnapshot SHALL have at least one Destination.

#### Scenario: Cluster has no Destinations

- **WHEN** a Cluster has an empty Destinations list
- **THEN** the validator adds error with code "CLUSTER_NO_DESTINATIONS"

### Requirement: TransformsJson SHALL be valid JSON format

If a Route has a non-empty TransformsJson, it SHALL be valid JSON representing a List of Dictionary<string, string>.

#### Scenario: TransformsJson is invalid JSON

- **WHEN** a Route has TransformsJson = "not-json"
- **THEN** the validator adds error with code "ROUTE_INVALID_TRANSFORMS"

### Requirement: Certificate SHALL have valid data

Every Certificate in the ConfigSnapshot SHALL have non-empty CertificateBytes, DomainName, and Thumbprint.

#### Scenario: Certificate has empty data

- **WHEN** a Certificate has empty CertificateBytes
- **THEN** the validator adds error with code "CERT_EMPTY_DATA"

#### Scenario: Certificate has empty domain

- **WHEN** a Certificate has empty or whitespace DomainName
- **THEN** the validator adds error with code "CERT_EMPTY_DOMAIN"

#### Scenario: Certificate is expired

- **WHEN** a Certificate has ExpiresAtUnixMs < current timestamp
- **THEN** the validator adds warning with code "CERT_EXPIRED"

### Requirement: Control Plane SHALL reject invalid config push

The Control Plane SHALL NOT push a ConfigSnapshot to Data Planes if validation fails.

#### Scenario: Validation fails blocks push

- **WHEN** ConfigValidator.Validate() returns IsValid = false
- **THEN** ConfigReplicationService SHALL log the errors and NOT push the config

#### Scenario: Validation passes allows push

- **WHEN** ConfigValidator.Validate() returns IsValid = true
- **THEN** ConfigReplicationService SHALL proceed with the push
