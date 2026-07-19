# cluster-name-is-id Specification

## Purpose
Define that the cluster ID is the user-provided cluster name, eliminating separate Id/Name fields.

## ADDED Requirements

### Requirement: Cluster ID equals name

The cluster SHALL use the user-provided name as its identity (Id). The `Name` field SHALL NOT exist separately — the Id IS the name.

#### Scenario: Create cluster with name as ID
- **WHEN** user sends `POST /api/apimanagement/clusters` with `{ "id": "prod-api", "destinations": [...] }`
- **THEN** the system SHALL create a cluster with `Id = "prod-api"`
- **AND** there SHALL NOT be a separate `Name` field

#### Scenario: Get cluster returns Id only
- **WHEN** user gets `GET /api/apimanagement/clusters/prod-api`
- **THEN** response SHALL contain `{ "id": "prod-api", ... }` without a `name` field

### Requirement: Cluster name uniqueness

The system SHALL enforce that cluster names are unique.

#### Scenario: Duplicate name rejected
- **WHEN** user creates a cluster with `id` that already exists
- **THEN** the system SHALL return `409 Conflict` or handle the database unique constraint violation

### Requirement: Cluster name is immutable

A cluster's name/ID SHALL NOT be changed after creation. To rename, the user SHALL delete and recreate the cluster.

#### Scenario: No rename endpoint
- **WHEN** user sends `PUT /api/apimanagement/clusters/prod-api` with a different `id` in the body
- **THEN** the system SHALL ignore the `id` field in the body (URL path is the identifier)
