## ADDED Requirements

### Requirement: Playwright E2E test infrastructure
The project SHALL include Playwright as a development dependency with initialized configuration for running end-to-end tests.

#### Scenario: Install and configure Playwright
- **WHEN** `pnpm add -D @playwright/test` is executed
- **AND** `npx playwright init` is run
- **THEN** a `playwright.config.ts` SHALL exist in the frontend project root
- **AND** a `e2e/` directory SHALL exist with test files

#### Scenario: Run E2E tests
- **WHEN** `npx playwright test` is executed
- **THEN** all tests in the `e2e/` directory SHALL execute against the configured test environment

### Requirement: Login flow E2E test
The E2E test suite SHALL cover the authentication flow.

#### Scenario: Login with valid credentials
- **WHEN** the test navigates to `/login`
- **AND** enters valid email and password
- **AND** submits the form
- **THEN** the test SHALL be redirected to `/dashboard`
- **AND** the user name SHALL be visible in the header

#### Scenario: Login with invalid credentials
- **WHEN** the test navigates to `/login`
- **AND** enters invalid email and password
- **AND** submits the form
- **THEN** an error message SHALL be displayed

### Requirement: Routes page E2E test
The E2E test suite SHALL cover the Routes page CRUD operations and filtering.

#### Scenario: Create a new route
- **WHEN** the test navigates to `/routes`
- **AND** clicks "添加路由" button
- **AND** fills in the form with name, path, and selects a cluster
- **AND** submits the form
- **THEN** the new route SHALL appear in the routes table
- **AND** a success toast SHALL be shown

#### Scenario: Edit an existing route
- **WHEN** the test navigates to `/routes`
- **AND** clicks the edit button on a route row
- **AND** modifies the route name in the form
- **AND** submits the form
- **THEN** the updated route name SHALL appear in the table
- **AND** a success toast SHALL be shown

#### Scenario: Delete a route
- **WHEN** the test navigates to `/routes`
- **AND** clicks the delete button on a route row
- **AND** confirms the deletion dialog
- **THEN** the route SHALL be removed from the table
- **AND** a success toast SHALL be shown

#### Scenario: Filter routes by status
- **WHEN** the test navigates to `/routes`
- **AND** selects "启用" from the status filter dropdown
- **THEN** only enabled routes SHALL be displayed in the table

#### Scenario: Search routes by name
- **WHEN** the test navigates to `/routes`
- **AND** types a route name in the search input
- **THEN** only matching routes SHALL be displayed in the table

### Requirement: Clusters page E2E test
The E2E test suite SHALL cover the Clusters page CRUD operations, destination management, and health check configuration.

#### Scenario: Create a new cluster
- **WHEN** the test navigates to `/clusters`
- **AND** clicks "添加集群" button
- **AND** fills in cluster name and load balancing policy
- **AND** adds at least one destination address
- **AND** submits the form
- **THEN** the new cluster card SHALL appear on the page
- **AND** a success toast SHALL be shown

#### Scenario: Edit a cluster
- **WHEN** the test navigates to `/clusters`
- **AND** clicks the edit button on a cluster card
- **AND** modifies the cluster name
- **AND** submits the form
- **THEN** the updated cluster name SHALL appear on the card
- **AND** a success toast SHALL be shown

#### Scenario: Delete a cluster
- **WHEN** the test navigates to `/clusters`
- **AND** clicks the delete button on a cluster card
- **AND** confirms the deletion dialog
- **THEN** the cluster SHALL be removed from the page
- **AND** a success toast SHALL be shown

#### Scenario: Manage cluster destinations
- **WHEN** the test navigates to `/clusters`
- **AND** edits a cluster
- **AND** adds a new destination with address
- **AND** saves the cluster
- **THEN** the new destination SHALL appear in the cluster card
- **AND** a success toast SHALL be shown

### Requirement: Certificates page E2E test
The E2E test suite SHALL cover the Certificates page upload, create, edit, and delete operations.

#### Scenario: Upload a certificate file
- **WHEN** the test navigates to `/certificates`
- **AND** clicks "上传证书" button
- **AND** selects a certificate file and enters domain name
- **AND** submits the upload form
- **THEN** the new certificate SHALL appear in the certificates table
- **AND** a success toast SHALL be shown

#### Scenario: Manually add a certificate
- **WHEN** the test navigates to `/certificates`
- **AND** clicks "手动添加" button
- **AND** fills in domain name, certificate type, and other fields
- **AND** submits the form
- **THEN** the new certificate SHALL appear in the certificates table
- **AND** a success toast SHALL be shown

#### Scenario: Delete a certificate
- **WHEN** the test navigates to `/certificates`
- **AND** clicks the delete button on a certificate row
- **AND** confirms the deletion dialog
- **THEN** the certificate SHALL be removed from the table
- **AND** a success toast SHALL be shown
