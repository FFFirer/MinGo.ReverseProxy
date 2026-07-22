## 1. Create GitHub Actions Workflow

- [x] 1.1 Create `.github/workflows/build.yml` based on `.gitea/workflows/build.yml` with BuildKit mirror config removed
- [x] 1.2 Verify the workflow YAML is valid GitHub Actions syntax

## 2. Remove npm Domestic Registry from Dockerfile

- [x] 2.1 Remove `RUN pnpm config set registry https://registry.npmmirror.com` line from Dockerfile
- [x] 2.2 Verify `NPM_REGISTRY` ARG and `COREPACK_NPM_REGISTRY` ENV remain intact
