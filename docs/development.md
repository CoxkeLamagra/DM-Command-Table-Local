# Development and Windows packaging

Use the Node version pinned in [`.node-version`](../.node-version) and pnpm 11.25.0. Work from the repository root with a separate development data directory. The default development database is `data/dm-command-table.sqlite`; the packaged Windows app uses `%LOCALAPPDATA%\DM Command Table`.

```sh
npm install --global pnpm@11.25.0
pnpm install --frozen-lockfile
pnpm dev
```

Open `http://localhost:3000`. SQLite initializes automatically. See [Architecture](architecture.md) for source layout and [Windows operations](operations.md) for the packaged app.

## Checks

```sh
pnpm verify
pnpm exec playwright install chromium
pnpm test:browser
```

`pnpm verify` runs dependency, formatting, lint, type, unit and production-build checks. Browser tests use disposable data on port 3100.

## Build the Windows ZIP

Use Linux or WSL with `curl`, `tar`, `unzip` and `zip`. After installing dependencies with the frozen lockfile, run:

```sh
pnpm build
node scripts/package-windows.mjs
```

The build writes the Windows x64 ZIP and its SHA-256 sidecar to `dist/`. Extract it on Windows and check first-time registration, campaign import, and startup with no internet access. The [Windows GitHub Actions workflow](../.github/workflows/windows-package.yml) checks the archive hash, extracts it with `Expand-Archive`, and smoke-tests the bundled executable, SQLite, image processing, and plain and image-inclusive campaign imports.

`pnpm clean` removes generated build and test outputs. It preserves source and application data.
