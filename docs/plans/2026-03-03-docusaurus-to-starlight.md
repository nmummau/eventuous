# Docusaurus to Astro/Starlight Migration — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the Docusaurus docs site with an Astro + Starlight site, preserving all content (current + v0.15), Algolia search, Mermaid diagrams, and themed images.

**Architecture:** Scaffold a fresh Astro + Starlight project in `docs/`, migrate ~44 current doc files and ~50 v0.15 files by converting frontmatter and replacing MDX component imports. Use `starlight-versions` for v0.15, `@astrojs/starlight-docsearch` for Algolia, and `starlight-client-mermaid` for diagrams.

**Tech Stack:** Astro, @astrojs/starlight, starlight-versions, @astrojs/starlight-docsearch, starlight-client-mermaid, pnpm

---

### Task 1: Back up current docs and scaffold Astro project

**Files:**
- Back up: `docs/` → `docs-docusaurus-backup/` (temporary, for reference during migration)
- Create: `docs/package.json`
- Create: `docs/astro.config.mjs`
- Create: `docs/tsconfig.json`
- Create: `docs/src/content.config.ts`

**Step 1: Copy current docs for reference**

```bash
cd /Users/alexey/dev/eventuous/eventuous
cp -r docs docs-docusaurus-backup
```

**Step 2: Remove Docusaurus files from docs/**

Remove everything except `plans/` directory:

```bash
cd docs
# Remove Docusaurus-specific files
rm -rf node_modules .docusaurus build
rm -f docusaurus.config.ts sidebars.ts babel.config.js tsconfig.json package.json pnpm-lock.yaml
rm -rf src versioned_docs versioned_sidebars versions.json
rm -rf static docs
```

**Step 3: Create package.json**

Create `docs/package.json`:

```json
{
  "name": "eventuous-docs",
  "version": "0.0.0",
  "private": true,
  "scripts": {
    "dev": "astro dev",
    "build": "astro build",
    "preview": "astro preview",
    "astro": "astro"
  }
}
```

**Step 4: Install dependencies**

```bash
cd /Users/alexey/dev/eventuous/eventuous/docs
pnpm add astro @astrojs/starlight starlight-versions @astrojs/starlight-docsearch starlight-client-mermaid
```

**Step 5: Create astro.config.mjs**

Create `docs/astro.config.mjs`:

```js
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightVersions from 'starlight-versions';
import starlightDocSearch from '@astrojs/starlight-docsearch';
import starlightMermaid from 'starlight-client-mermaid';

export default defineConfig({
  site: 'https://eventuous.dev',
  integrations: [
    starlight({
      title: 'Eventuous',
      logo: {
        src: './src/assets/logo.svg',
      },
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/eventuous/eventuous' },
        { icon: 'discord', label: 'Discord', href: 'https://discord.gg/ZrqM6vnnmf' },
      ],
      customCss: ['./src/styles/custom.css'],
      plugins: [
        starlightVersions({
          versions: [{ slug: '0.15' }],
        }),
        starlightDocSearch({
          appId: 'YQSSKN21VQ',
          apiKey: '8985834538ee1103dfbee3358e1a4bfe',
          indexName: 'eventuous',
        }),
        starlightMermaid(),
      ],
      sidebar: [
        { label: 'Introduction', slug: 'intro' },
        { label: "What's New", slug: 'whats-new' },
        { label: 'Prologue', autogenerate: { directory: 'prologue' } },
        { label: 'Domain', autogenerate: { directory: 'domain' } },
        { label: 'Persistence', autogenerate: { directory: 'persistence' } },
        { label: 'Application', autogenerate: { directory: 'application' } },
        { label: 'Subscriptions', autogenerate: { directory: 'subscriptions' } },
        { label: 'Read Models', autogenerate: { directory: 'read-models' } },
        { label: 'Producers', autogenerate: { directory: 'producers' } },
        { label: 'Gateway', autogenerate: { directory: 'gateway' } },
        { label: 'Diagnostics', autogenerate: { directory: 'diagnostics' } },
        { label: 'Infrastructure', autogenerate: { directory: 'infra' } },
        { label: 'FAQ', autogenerate: { directory: 'faq' } },
      ],
      head: [
        {
          tag: 'link',
          attrs: { rel: 'icon', href: '/favicon.ico', sizes: '32x32' },
        },
      ],
      editLink: {
        baseUrl: 'https://github.com/eventuous/eventuous/edit/dev/docs/',
      },
    }),
  ],
});
```

**Step 6: Create tsconfig.json**

Create `docs/tsconfig.json`:

```json
{
  "extends": "astro/tsconfigs/strict"
}
```

**Step 7: Create content.config.ts**

Create `docs/src/content.config.ts`:

```ts
import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';
import { defineCollection } from 'astro:content';
import { docsVersionsLoader } from 'starlight-versions/loader';

export const collections = {
  docs: defineCollection({ loader: docsLoader(), schema: docsSchema() }),
  versions: defineCollection({ loader: docsVersionsLoader() }),
};
```

**Step 8: Create minimal custom CSS**

Create `docs/src/styles/custom.css`:

```css
/* Eventuous custom styles — start with minimal overrides */
```

**Step 9: Create directory structure**

```bash
mkdir -p docs/src/assets
mkdir -p docs/src/components
mkdir -p docs/src/content/docs
mkdir -p docs/public
```

**Step 10: Copy static assets**

```bash
cp docs-docusaurus-backup/static/img/favicon.ico docs/public/
cp docs-docusaurus-backup/static/img/favicon-16x16.png docs/public/
cp docs-docusaurus-backup/static/img/favicon-32x32.png docs/public/
cp docs-docusaurus-backup/static/img/apple-touch-icon.png docs/public/
cp docs-docusaurus-backup/static/img/android-chrome-192x192.png docs/public/
cp docs-docusaurus-backup/static/img/android-chrome-512x512.png docs/public/
cp docs-docusaurus-backup/static/img/social-card.png docs/public/
cp docs-docusaurus-backup/static/img/site.webmanifest docs/public/
cp docs-docusaurus-backup/static/img/logo.svg docs/src/assets/
cp docs-docusaurus-backup/static/img/logo.png docs/src/assets/
cp docs-docusaurus-backup/static/_redirects docs/public/
```

**Step 11: Verify the project builds**

```bash
cd /Users/alexey/dev/eventuous/eventuous/docs
pnpm dev
```

Expected: Dev server starts with an empty docs site. Stop it with Ctrl+C.

**Step 12: Commit**

```bash
git add docs/
git commit -m "docs: scaffold Astro + Starlight project"
```

---

### Task 2: Create custom Astro components

**Files:**
- Create: `docs/src/components/ThemedImage.astro`
- Create: `docs/src/components/Highlight.astro`

**Step 1: Create ThemedImage component**

This replaces Docusaurus `@theme/ThemedImage`. In Docusaurus, images were imported as ESM modules. In Starlight, we'll reference images by relative path strings.

Create `docs/src/components/ThemedImage.astro`:

```astro
---
interface Props {
  alt: string;
  lightSrc: string;
  darkSrc: string;
}

const { alt, lightSrc, darkSrc } = Astro.props;
---

<picture>
  <source srcset={darkSrc} media="(prefers-color-scheme: dark)" />
  <img src={lightSrc} alt={alt} />
</picture>

<style>
  picture {
    display: block;
    margin: 1rem 0;
  }
  img {
    max-width: 100%;
    height: auto;
  }
</style>
```

Note: Starlight uses a `data-theme` attribute, so we may also need:

```astro
---
interface Props {
  alt: string;
  lightSrc: string;
  darkSrc: string;
}

const { alt, lightSrc, darkSrc } = Astro.props;
---

<picture class="themed-image">
  <img class="light-only" src={lightSrc} alt={alt} />
  <img class="dark-only" src={darkSrc} alt={alt} />
</picture>

<style>
  .themed-image img {
    max-width: 100%;
    height: auto;
    margin: 1rem 0;
  }
  :global([data-theme='dark']) .light-only,
  :global(:not([data-theme='dark'])) .dark-only {
    display: none;
  }
</style>
```

Test by checking how Starlight handles theme toggling and adjust the CSS selectors accordingly.

**Step 2: Create Highlight component**

Create `docs/src/components/Highlight.astro`:

```astro
---
interface Props {
  color: string;
}

const { color } = Astro.props;
---

<span style={`background-color: ${color}; border-radius: 2px; color: #fff; padding: 0.2rem;`}>
  <slot />
</span>
```

**Step 3: Commit**

```bash
git add docs/src/components/
git commit -m "docs: add ThemedImage and Highlight Astro components"
```

---

### Task 3: Migrate current (next) docs — pure Markdown files

Migrate the ~32 pure `.md` files from `docs-docusaurus-backup/docs/` to `docs/src/content/docs/`. These files need only frontmatter conversion (no MDX component changes).

**Files:**
- Copy from: `docs-docusaurus-backup/docs/` (all `.md` files and their co-located images)
- Copy to: `docs/src/content/docs/`

**Step 1: Copy all content directories with their images**

```bash
# Copy entire directory structure preserving images
cd /Users/alexey/dev/eventuous/eventuous
for dir in prologue domain persistence application subscriptions read-models producers gateway diagnostics infra faq; do
  cp -r docs-docusaurus-backup/docs/$dir docs/src/content/docs/
done
```

**Step 2: Convert frontmatter for each `.md` file**

For each `.md` file, apply these transformations:
- Remove `id` field
- Rename `sidebar_position` → `sidebar.order`
- Rename `sidebar_label` → `sidebar.label`
- Remove `weight` field (use `sidebar.order` instead, preserving relative ordering)
- Keep `title` and `description` as-is

Example — before (`domain/aggregate.md`):

```yaml
---
title: "Aggregate"
description: "..."
sidebar_position: 1
---
```

After:

```yaml
---
title: "Aggregate"
description: "..."
sidebar:
  order: 1
---
```

Files to convert (pure `.md` with `sidebar_position`):

| File | sidebar_position → order |
|------|--------------------------|
| `prologue/introduction.md` | 1 |
| `prologue/quick-start.md` | 2 |
| `domain/aggregate.md` | 1 |
| `domain/state.md` | 2 |
| `domain/domain-events.md` | 3 |
| `persistence/aggregate-stream.md` | 1 |
| `persistence/event-store.md` | 2 |
| `application/app-service.md` | 1 |
| `application/func-service.md` | 3 |
| `application/command-api.md` | 3 |
| `infra/esdb/index.md` | 1 |
| `infra/mongodb/index.md` | 2 |
| `infra/postgres/index.md` | 3 |
| `infra/mssql/index.md` | 4 |
| `infra/sqlite/index.md` | 5 |
| `infra/rabbitmq/index.md` | 5 |
| `infra/pubsub/index.md` | 6 |

Files with `weight` instead of `sidebar_position` — convert to `sidebar.order`:

| File | weight → order |
|------|----------------|
| `producers/implementation.md` | 1 (was weight: 100) |
| `gateway/implementation/index.md` | 1 (was weight: 10) |

Files with no position/weight (title + description only) — leave as-is:

- `persistence/serialisation.md`
- `subscriptions/eventhandler/index.md`
- `subscriptions/sub-base/index.md` (has sidebar_position: 2)
- `subscriptions/subs-diagnostics/index.md`
- `read-models/rm-concept.md`
- `read-models/supported-projectors.md`
- `producers/providers.md`
- `infra/kafka/index.md`
- `infra/elastic/index.md`
- `faq/compare.md`
- `faq/compatibility.md`
- `faq/persistence.md`
- `diagnostics/details.md` (no frontmatter at all — add `title: "Diagnostics"`)

**Step 3: Verify admonitions work**

Starlight supports `:::note`, `:::tip`, `:::caution`, `:::danger` natively via remark-directive. Docusaurus syntax is the same. No changes needed.

However, check if Docusaurus uses `:::caution` where Starlight expects `:::caution` — they should match. Starlight maps: `note`, `tip`, `caution`, `danger`.

**Step 4: Commit**

```bash
git add docs/src/content/docs/
git commit -m "docs: migrate pure Markdown files with converted frontmatter"
```

---

### Task 4: Migrate current (next) docs — MDX files

Migrate the ~12 `.mdx` files. These need frontmatter conversion AND component import/usage changes.

**Files to migrate:**

#### 4a: intro.mdx and whats-new.mdx (top-level, no component imports)

Copy from backup, convert frontmatter:

`intro.mdx` — has `sidebar_position: 1`. Convert to:
```yaml
---
title: "Eventuous"
sidebar:
  order: 0
---
```

`whats-new.mdx` — has `sidebar_position: 2`. Convert to:
```yaml
---
title: "What's new"
sidebar:
  order: 1
---
```

The `intro.mdx` file contains a YouTube `<iframe>` — this works as-is in Astro MDX.

#### 4b: Files using DocCardList (remove component, simplify)

These files import `DocCardList` from `@theme/DocCardList` and render it. In Starlight, the `autogenerate` sidebar config handles this automatically. Convert these to simple index pages.

Files:
- `persistence/index.mdx`
- `application/index.mdx`
- `producers/index.mdx`
- `gateway/index.mdx`

For each:
1. Remove the `import DocCardList from '@theme/DocCardList'` line
2. Remove the `<DocCardList />` component usage
3. Convert frontmatter (remove `weight`/`position`, keep title/description)

Example — `persistence/index.mdx` before:
```mdx
---
title: "Persistence"
weight: 300
description: "..."
---
import DocCardList from '@theme/DocCardList';

Some text...

:::info
Info block
:::

<DocCardList />
```

After:
```mdx
---
title: "Persistence"
description: "..."
---

Some text...

:::info
Info block
:::
```

#### 4c: Files using ThemedImage (rewrite imports and usage)

These files use Docusaurus `ThemedImage` with ESM image imports. Convert to our custom Astro `ThemedImage` component with path strings.

Files:
- `prologue/the-right-way/index.mdx`
- `persistence/aggregate-store/index.mdx`
- `subscriptions/subs-concept/index.mdx`
- `subscriptions/checkpoint/index.mdx`
- `subscriptions/pipes/index.mdx`

For each file:
1. Remove `import ThemedImage from '@theme/ThemedImage'`
2. Remove all ESM image imports (`import darkUrl from './images/...'`)
3. Add `import ThemedImage from '../../../components/ThemedImage.astro'` (adjust relative path)
4. Convert `<ThemedImage sources={{ light: varName, dark: varName }} />` to `<ThemedImage lightSrc="./images/file-light.png" darkSrc="./images/file-dark.png" />`

Example — `prologue/the-right-way/index.mdx` before:
```mdx
import ThemedImage from '@theme/ThemedImage';
import darkUrl from "./images/the-right-way-dark.png";
import lightUrl from "./images/the-right-way.png";

<ThemedImage
    alt="Eventuous way"
    sources={{
        light: lightUrl,
        dark: darkUrl,
    }}
/>
```

After:
```mdx
import ThemedImage from '../../../components/ThemedImage.astro';

<ThemedImage
    alt="Eventuous way"
    lightSrc="./images/the-right-way.png"
    darkSrc="./images/the-right-way-dark.png"
/>
```

Note: Verify that relative image paths resolve correctly in Astro. If not, images may need to go in `public/` with absolute paths.

#### 4d: File using Highlight (rewrite import)

File: `application/command-map.mdx`

1. Remove `import Highlight from "@site/src/components/highlight"`
2. Add `import Highlight from '../../components/Highlight.astro'` (adjust path)
3. Usage syntax stays the same: `<Highlight color="blue">**text**</Highlight>`

**Step 5: Commit**

```bash
git add docs/src/content/docs/
git commit -m "docs: migrate MDX files with converted components"
```

---

### Task 5: Migrate version 0.15 docs

The `starlight-versions` plugin archives docs. We need to get the v0.15 content into the right place for the plugin to manage it.

**Files:**
- Source: `docs-docusaurus-backup/versioned_docs/version-0.15/` (50 files)
- Target: Managed by `starlight-versions` plugin

**Step 1: Understand starlight-versions archiving**

The `starlight-versions` plugin works by:
1. You configure `versions: [{ slug: '0.15' }]` in astro.config.mjs
2. On first dev server start, it creates an archive of current docs as v0.15
3. Current docs become the "latest" version

So we need to:
1. First populate `src/content/docs/` with the v0.15 content
2. Start the dev server to let the plugin archive it
3. Then replace `src/content/docs/` with the current/next content

**Step 2: Temporarily populate docs with v0.15 content**

```bash
# Clear current docs
rm -rf docs/src/content/docs/*

# Copy v0.15 content
cp -r docs-docusaurus-backup/versioned_docs/version-0.15/* docs/src/content/docs/
```

**Step 3: Convert v0.15 frontmatter**

Apply the same frontmatter conversion rules as Task 3 and Task 4 to all v0.15 files. The v0.15 version has some extra files compared to current:
- `application/_result.mdx`, `_service_http.mdx`, `_service_no_throw.mdx`, `_service_registration.mdx` (partial MDX files imported by other files)
- `diagnostics/index.mdx`, `logs.md`, `metrics.md`, `opentelemetry.md`, `traces.md`
- Different file extensions for some files (`.mdx` vs `.md`)

Apply the same component migration rules (ThemedImage, DocCardList, Highlight).

**Step 4: Start dev server to archive v0.15**

```bash
cd /Users/alexey/dev/eventuous/eventuous/docs
pnpm dev
```

The plugin should create the v0.15 archive. Stop the dev server.

**Step 5: Replace with current (next) content**

```bash
rm -rf docs/src/content/docs/*
cp -r docs-docusaurus-backup/docs/* docs/src/content/docs/
```

Then re-apply the frontmatter and component migrations from Tasks 3 and 4 to the current docs.

**Step 6: Verify both versions work**

```bash
pnpm dev
```

Check that:
- The version switcher appears
- Current docs render correctly
- v0.15 docs render correctly

**Step 7: Commit**

```bash
git add docs/
git commit -m "docs: add version 0.15 archive via starlight-versions"
```

---

### Task 6: Verify build and fix issues

**Step 1: Run full build**

```bash
cd /Users/alexey/dev/eventuous/eventuous/docs
pnpm build
```

**Step 2: Fix any build errors**

Common issues to watch for:
- Broken relative image paths in MDX
- Admonition syntax differences (Starlight uses `:::note[Custom title]` for titled admonitions vs Docusaurus `:::note Custom title`)
- MDX expression syntax differences between Docusaurus MDX v3 and Astro MDX
- Missing frontmatter `title` field (required by Starlight)
- Mermaid code blocks not rendering

**Step 3: Preview the built site**

```bash
pnpm preview
```

Manually verify:
- All sidebar sections appear with correct ordering
- Mermaid diagrams render (check `application/app-service`, `command-map`, `func-service`, `command-api`)
- Themed images toggle between light/dark mode
- Algolia search works (may need index re-crawl)
- `_redirects` file is in the output
- Admonitions render correctly
- Code blocks have C# syntax highlighting

**Step 4: Commit any fixes**

```bash
git add docs/
git commit -m "docs: fix migration issues found during build verification"
```

---

### Task 7: Clean up

**Step 1: Remove the backup directory**

```bash
cd /Users/alexey/dev/eventuous/eventuous
rm -rf docs-docusaurus-backup
```

**Step 2: Update CLAUDE.md**

Update the Documentation Site section in `CLAUDE.md` to reflect the new Astro/Starlight setup:
- Change build commands from Docusaurus to Astro
- Update content path from `docs/docs/` to `docs/src/content/docs/`
- Note Starlight frontmatter format

Modify: `/Users/alexey/dev/eventuous/eventuous/CLAUDE.md` (the Documentation Site section)

**Step 3: Update .gitignore if needed**

Check if `docs/.gitignore` needs updating for Astro (`.astro/` cache directory, `dist/` output).

**Step 4: Final build verification**

```bash
cd /Users/alexey/dev/eventuous/eventuous/docs
pnpm build
```

Expected: Clean build with no errors.

**Step 5: Commit**

```bash
git add -A
git commit -m "docs: clean up after Docusaurus to Starlight migration"
```

---

## Task Dependencies

```
Task 1 (scaffold) → Task 2 (components) → Task 3 (markdown) → Task 4 (MDX) → Task 5 (v0.15) → Task 6 (verify) → Task 7 (cleanup)
```

All tasks are sequential — each depends on the previous.

## Notes

- The `starlight-versions` plugin workflow (Task 5) may need adjustment based on how the plugin actually archives versions at runtime. Read the plugin docs carefully during execution.
- Image paths in MDX: Astro may handle relative imports differently than Docusaurus. If co-located images don't resolve, move them to `public/images/` and use absolute paths.
- The Algolia search index will need to be re-crawled by Algolia after deployment since the HTML structure changes.
- C# syntax highlighting: Starlight uses Shiki (not Prism). Shiki supports `csharp` out of the box, but the language identifier might need to be `csharp` or `cs` — verify.
