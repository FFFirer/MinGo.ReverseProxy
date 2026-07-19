# Seed Data

## ADDED Requirements

### Development seed data

#### Scenario: First run creates admin
- Given the database is empty
- When the application starts in Development mode
- Then a default admin account is created

#### Scenario: Subsequent runs skip seeding
- Given the database already has users
- When the application starts in Development mode
- Then no new users are created

### Requirement: Cluster seed data uses names as IDs

When the system initializes sample data, cluster IDs SHALL be human-readable names rather than GUIDs.

#### Scenario: Seed clusters use names as IDs
- **WHEN** the system seeds sample data
- **THEN** clusters SHALL have IDs like `"user-cluster"`, `"product-cluster"` (not GUIDs)
