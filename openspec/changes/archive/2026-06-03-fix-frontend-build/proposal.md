## Why

pnpm v10+ supply-chain security policy rejects lockfile entries where packages were published within the minimumReleaseAge cutoff window. The frontend build fails in Docker because `electron-to-chromium` and `node-releases` versions in the lockfile are too recent relative to the build time.

## What Changes

- Add `minimum-release-age=0` to frontend `.npmrc` to disable the age check
- Regenerate `pnpm-lock.yaml` with clean resolution
- Commit both files

## Impact

- Only affects frontend build pipeline
- Disables pnpm's package age verification (acceptable for local/dev/build scenarios)
- Lockfile updated to latest resolvable versions
