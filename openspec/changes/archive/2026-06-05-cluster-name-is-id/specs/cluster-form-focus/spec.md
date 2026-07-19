# cluster-form-focus Specification (Delta)

## ADDED Requirements

### Requirement: Cluster form uses Id only

The cluster form SHALL have a single "集群名称" input that serves as both the name and the ID. There SHALL NOT be a separate hidden ID field.

#### Scenario: Create form
- **WHEN** user opens the "add cluster" form
- **THEN** there SHALL be one text input: "集群名称" (which becomes the cluster's Id)
- **AND** there SHALL NOT be a separately stored GUID

#### Scenario: Edit form
- **WHEN** user opens the "edit cluster" form
- **THEN** the cluster Id SHALL be displayed as read-only text (cannot be changed)
- **AND** the form SHALL NOT contain a name input separate from Id
