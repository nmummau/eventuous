# Docs Versioning & Authoring Guide

This file is for Claude Code. It documents how the Eventuous docs site versioning works so docs can be updated correctly.

## Version Structure

Managed by the `starlight-versions` plugin in `astro.config.mjs`.

```
docs/src/content/docs/          ← "current" version (root, shown by default)
docs/src/content/docs/0.15/     ← archived v0.15 snapshot
docs/src/content/docs/next/     ← preview placeholder for future version
docs/src/content/versions/      ← sidebar configs per version (JSON)
```

- **Root** (`docs/src/content/docs/`) is always the current stable version.
- **Archived versions** (e.g. `0.15/`) are read-only snapshots. Don't edit unless fixing a bug in old docs.
- **`next/`** is a minimal placeholder. When preparing a new release, content accumulates here, then gets promoted to root.

## Config in astro.config.mjs

```js
starlightVersions({
  current: { label: 'v0.16 (Stable)' },
  versions: [
    { slug: '0.15', label: 'v0.15' },
    { slug: 'next', label: 'Preview' },
  ],
}),
```

Each entry in `versions` must have a matching directory under `docs/src/content/docs/{slug}/` and a sidebar config at `docs/src/content/versions/{slug}.json`.

## Relative Path Rules

Component imports and markdown links use different base paths. Getting these wrong breaks the build.

### Component imports (filesystem-relative)

From `docs/src/content/docs/` to `docs/src/components/`:

| File location | Import path |
|---|---|
| Root subdirectory (e.g. `subscriptions/checkpoint.mdx`) | `../../../components/Foo.astro` (3 levels) |
| Versioned subdirectory (e.g. `0.15/subscriptions/checkpoint.mdx`) | `../../../../components/Foo.astro` (4 levels) |

### Markdown links (doc-tree-relative)

Internal doc links resolve within the doc tree, not the filesystem:

| File location | Link to another section |
|---|---|
| Root subdirectory (e.g. `infra/esdb.md`) | `../../application/app-service` (2 levels) |
| `next/` subdirectory (e.g. `next/infra/esdb.md`) | `../../../application/app-service` (3 levels) |

### Hero image (index.mdx)

| File location | Image path to `src/assets/logo.png` |
|---|---|
| Root `index.mdx` | `../../assets/logo.png` |
| `0.15/index.mdx` | `../../../assets/logo.png` |

## How to Release a New Version

To promote `next/` to a new stable version (e.g. v0.17):

1. **Archive current root** → copy root content (excluding `next/` and version dirs) into a new directory (e.g. `0.16/`). Create a matching `src/content/versions/0.16.json` sidebar config (copy from `next.json` template).
2. **Replace root with `next/`** → delete root content files, copy `next/` to root.
3. **Fix relative paths** → reduce all `../` depths by 1 in the new root files (component imports: 4→3, markdown links: 3→2, hero image: 3→2).
4. **Fix archived version paths** → increase all `../` depths by 1 in the archived directory (component imports: 3→4).
5. **Update `astro.config.mjs`** → change `current.label`, add archived version to `versions` array.
6. **Reset `next/`** → delete all content, create a single `whats-new.mdx` placeholder. Update `next.json` sidebar to only reference `whats-new`.
7. **Build and verify** → `pnpm build` must pass. Check page count and spot-check version selector.

> **Do not** rely on the plugin's auto-snapshot feature (`ensureNewVersion`). It fails on MDX files with complex Astro expressions. Always snapshot manually.

## Adding New Doc Pages

- Add `.md` or `.mdx` files to the appropriate topic directory under root docs.
- The sidebar auto-generates from directory contents. Use `sidebar.order` in frontmatter to control ordering.
- For new infrastructure providers, add to `infra/` with a descriptive filename (e.g. `azure-service-bus.md`).
- If adding pages to `next/` for a future version, remember that all relative paths need one extra `../` compared to root.
