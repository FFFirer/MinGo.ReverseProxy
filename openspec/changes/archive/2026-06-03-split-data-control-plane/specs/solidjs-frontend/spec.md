## ADDED Requirements

### Requirement: SPA frontend built with SolidJS and TailwindCSS v4

The frontend SHALL be a Single Page Application built with SolidJS (UI framework), TailwindCSS v4 (CSS-first configuration via `@theme`), Vite (build tool), and TypeScript. The design SHALL strictly follow the visual style and layout of `docs/control-plane-prototype.html`, including: top navigation bar with logo/status/search/theme toggle/notifications/user avatar, collapsible sidebar with 8 navigation items, dark mode support, card-based content areas, and responsive layout for mobile/tablet/desktop.

#### Scenario: Frontend renders Dashboard page with stat cards and charts
- **WHEN** user navigates to `/`
- **THEN** the Dashboard page displays 4 stat cards (total requests, error rate, avg response time, active services), a request trend chart area, service status bars, and a recent requests table

#### Scenario: Frontend renders Routes management page
- **WHEN** user navigates to `/routes`
- **THEN** the Routes page shows a search bar, status filter, and a table with route name/path/cluster/enabled status/action buttons (edit/delete)

#### Scenario: Frontend renders Clusters management page
- **WHEN** user navigates to `/clusters`
- **THEN** the Clusters page shows a grid of cluster cards, each containing destination entries with health status, address, and action buttons

#### Scenario: Frontend renders Login page
- **WHEN** user navigates to `/login`
- **THEN** the Login page renders a form with email and password inputs, submits to `POST /api/auth/login` with `credentials: 'include'`, and redirects to dashboard on success

#### Scenario: Frontend preserves dark mode preference
- **WHEN** user toggles dark mode via the theme button in the header
- **THEN** the preference is saved to `localStorage` and the `dark` class is applied to the HTML element

### Requirement: Frontend API client uses Cookie authentication

The frontend SHALL use the Fetch API with `credentials: 'include'` for all API calls to the control plane backend. The API client SHALL handle 401 responses by redirecting to the login page. The base URL of the control plane API SHALL be configurable via environment variable or build-time configuration.

#### Scenario: API client sends Cookie with requests
- **WHEN** the frontend makes any API request to the control plane
- **THEN** the request includes `credentials: 'include'` to send the authentication Cookie

#### Scenario: Unauthenticated request redirects to login
- **WHEN** the API client receives a 401 response
- **THEN** it redirects the user to `/login`

### Requirement: Frontend pages cover all management functions

The frontend SHALL include the following pages, matching the prototype design:
- Dashboard (`/`): Overview stats, charts, service status, recent requests
- Routes (`/routes`): Route CRUD with search/filter
- Clusters (`/clusters`): Cluster CRUD with destinations
- Security (`/security`): API keys, IP access control, rate limiting (UI only, backend TBD)
- Monitoring (`/monitoring`): Time-series charts with time range selector, alert records
- Logs (`/logs`): Real-time log viewer with search/filter/export
- Instances (`/instances`): Instance list with stats and status
- Certificates (`/certificates`): Certificate CRUD with upload/parse
- Settings (`/settings`): Gateway global configuration
- Login (`/login`): Authentication form
- Profile (`/profile`): User profile information

#### Scenario: All navigation items navigate to correct pages
- **WHEN** user clicks any sidebar navigation item
- **THEN** the corresponding page component renders without full page reload (SPA routing)
