## ADDED Requirements

### Requirement: Automated container image build

The CI pipeline SHALL build container images for all components using Gitea Actions.

#### Scenario: Release triggered build
- **WHEN** a new release is published (or prereleased/released) in Gitea
- **THEN** the pipeline SHALL build 3 images in parallel using matrix strategy
- **AND** tag each image with the release tag name (e.g., `1.0.8`)
- **AND** also tag each image with `latest`

#### Scenario: Manual trigger with dev version
- **WHEN** workflow is manually dispatched via `workflow_dispatch`
- **THEN** the pipeline SHALL build 3 images in parallel
- **AND** tag each image with `dev-{commit_sha:7}-{YYYYMMDDHHmmSS}`

### Requirement: Multi-component parallel build

The pipeline SHALL build three independent images simultaneously via matrix strategy.

#### Scenario: Matrix build execution
- **WHEN** the pipeline starts
- **THEN** it SHALL build component `cp` using `ControlPlane.Dockerfile`
- **AND** component `dp` using `DataPlane.Dockerfile`
- **AND** component `fe` using `frontend/min-go-console/Dockerfile`
- **AND** all three builds SHALL run in parallel

#### Scenario: Partial failure handling
- **WHEN** one component build fails
- **THEN** the pipeline SHALL continue building other components
- **AND** the overall workflow SHALL be marked as failed
- **AND** already-built images SHALL NOT be removed

### Requirement: Registry authentication and push

The pipeline SHALL authenticate to the private Docker registry and push built images.

#### Scenario: Registry login
- **WHEN** the pipeline runs
- **THEN** it SHALL read registry URL from `vars.DOCKER_REGISTRY`
- **AND** read username from `vars.DOCKER_USERNAME`
- **AND** read password from `secrets.DOCKER_PASSWORD`
- **AND** login to the registry using `docker/login-action`

#### Scenario: Image push
- **WHEN** images are built
- **THEN** the pipeline SHALL push all tagged images to `registry.private.fffirer.top:9081/1mingo/reverseproxy-{cp,dp,fe}`
- **AND** use `docker/build-push-action` with `push: true`

### Requirement: Version tagging convention

The pipeline SHALL apply consistent version tags following release or manual trigger patterns.

#### Scenario: Release tag extraction
- **WHEN** triggered by a release event
- **THEN** the version tag SHALL be the full git ref name (`GITHUB_REF#refs/*/`)

#### Scenario: Dev tag extraction
- **WHEN** triggered by `workflow_dispatch`
- **THEN** the version tag SHALL be `dev-{short_sha}-{timestamp}`
