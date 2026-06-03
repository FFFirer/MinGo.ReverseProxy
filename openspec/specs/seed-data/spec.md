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

