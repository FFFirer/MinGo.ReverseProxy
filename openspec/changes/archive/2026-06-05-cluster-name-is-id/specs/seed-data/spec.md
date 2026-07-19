# Seed Data Specification (Delta)

## MODIFIED Requirements

### Requirement: Development seed data

When the system initializes sample data, cluster IDs SHALL be human-readable names rather than GUIDs.

#### Scenario: Seed clusters use names as IDs
- **WHEN** the system seeds sample data
- **THEN** clusters SHALL have IDs like `"user-cluster"`, `"product-cluster"` (not GUIDs)
