# Identity Auth Specification

## Context

MinGo.ReverseProxy 为 Blazor Server 架构（非前后端分离），使用 ASP.NET Core Identity Cookie 认证方案。

## ADDED Requirements

### Requirement: User can register with email and password
The system SHALL allow new users to register using an email address and password through the Blazor registration page.

#### Scenario: Successful registration
- **WHEN** User navigates to `/Register` and submits valid email and password
- **THEN** system creates a new user, sets authentication Cookie, and redirects to homepage

#### Scenario: Duplicate email registration
- **WHEN** User submits an already registered email on the registration page
- **THEN** system returns validation error on the registration form

#### Scenario: Weak password
- **WHEN** User submits a password shorter than the configured minimum length
- **THEN** system returns password validation errors on the form

### Requirement: User can login with email and password
The system SHALL allow registered users to authenticate via the Blazor login page with Cookie-based authentication.

#### Scenario: Successful login with Cookie
- **WHEN** User navigates to `/Login` and submits valid credentials
- **THEN** system validates credentials, sets authentication Cookie, and redirects to the homepage or return URL

#### Scenario: Invalid credentials
- **WHEN** User submits invalid email or password on the login page
- **THEN** system returns an error message on the login form

### Requirement: User can logout
The system SHALL allow authenticated users to log out and clear their authentication Cookie.

#### Scenario: Successful logout
- **WHEN** Authenticated user clicks the logout button
- **THEN** system clears the authentication Cookie and redirects to the login page

### Requirement: Protected pages require authentication
The system SHALL enforce authentication on protected pages and redirect unauthenticated users to the login page.

#### Scenario: Access protected page without authentication
- **WHEN** An unauthenticated user attempts to access a protected page
- **THEN** system redirects to `/Login` with a return URL

#### Scenario: Access protected page with valid Cookie
- **WHEN** An authenticated user (with valid Cookie) accesses a protected page
- **THEN** system renders the page normally

### Requirement: Blazor authorization integration
The system SHALL integrate authentication with Blazor Server's authorization system.

#### Scenario: CascadingAuthenticationState works
- **WHEN** A Blazor component uses `AuthorizeView` or `[Authorize]` attribute
- **THEN** the component correctly reflects the user's authentication state

## Authentication Flow

### Login Flow
1. User navigates to `/Login` (Blazor page)
2. User submits email + password via the form
3. Server calls `SignInManager.PasswordSignInAsync()`
4. On success: Cookie is set automatically, redirect to return URL or homepage
5. On failure: Error message displayed on the form

### Cookie Configuration
| Setting | Value |
|---------|-------|
| LoginPath | `/Login` |
| LogoutPath | `/Logout` |
| AccessDeniedPath | `/AccessDenied` |
| ExpireTimeSpan | 7 days |
| SlidingExpiration | true |
| HttpOnly | true |
| SecurePolicy | Always |

## API Endpoints

| Endpoint | Method | Auth Required | Description |
|----------|--------|-------------|-------------|
| `/Login` | GET/POST | No | Login page (Blazor) |
| `/Register` | GET/POST | No | Registration page (Blazor) |
| `/Logout` | POST | Yes | Logout action |

Note: No RESTful auth API endpoints are exposed. Authentication flows through Blazor Server rendered pages.
