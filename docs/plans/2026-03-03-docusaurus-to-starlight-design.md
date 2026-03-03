# Docusaurus to Astro/Starlight Migration Design

**Date**: 2026-03-03
**Status**: Approved

## Context

Migrate the Eventuous documentation site (eventuous.dev) from Docusaurus v3 to Astro + Starlight. Motivations: flexibility and future-proofing.

## Decisions

- **Approach**: In-place migration — replace `docs/` directory contents
- **Framework**: Astro + Starlight + `starlight-versions` plugin
- **Versions**: 0.15 (archived via starlight-versions) + current/next. Version 0.14 dropped.
- **Homepage**: Docs-first — `intro.mdx` is the landing page, no custom homepage
- **Design**: Fresh look — start with Starlight defaults, iterate on colors later
- **Deployment**: Cloudflare Pages (already in use), `_redirects` carried over in `public/`

## Project Structure

```
docs/
├── astro.config.mjs
├── package.json
├── tsconfig.json
├── src/
│   ├── assets/              (logos, themed images)
│   ├── components/          (ThemedImage, Highlight Astro components)
│   ├── content/
│   │   └── docs/            (current "next" version)
│   │       ├── index.mdx
│   │       ├── whats-new.mdx
│   │       ├── prologue/
│   │       ├── domain/
│   │       ├── persistence/
│   │       ├── application/
│   │       ├── subscriptions/
│   │       ├── read-models/
│   │       ├── producers/
│   │       ├── gateway/
│   │       ├── diagnostics/
│   │       ├── infra/
│   │       └── faq/
│   ├── content.config.ts    (uses docsVersionsLoader from starlight-versions)
│   └── styles/
│       └── custom.css
├── public/
│   ├── favicon.ico (+ other favicons)
│   ├── social-card.png
│   ├── site.webmanifest
│   └── _redirects
```

## Content Migration

### Frontmatter conversion

```yaml
# Docusaurus                    # Starlight
---                              ---
id: aggregate                    # removed (slug from path)
title: Aggregate                 title: Aggregate
sidebar_label: Aggregate         sidebar:
sidebar_position: 1                label: Aggregate
description: "..."                 order: 1
---                              description: "..."
                                 ---
```

### MDX component replacements

| Docusaurus | Starlight |
|---|---|
| `DocCardList` from `@theme/DocCardList` | Removed — Starlight auto-generates index pages via `autogenerate` sidebar config |
| `ThemedImage` from `@theme/ThemedImage` | Custom Astro `<ThemedImage>` component using `<picture>` + `prefers-color-scheme` |
| `Highlight` from `@site/src/components/highlight` | Custom Astro `<Highlight>` component — same inline span logic |
| `:::tip` / `:::note` / `:::caution` admonitions | Same syntax — Starlight supports natively |
| Mermaid code blocks | `starlight-mermaid` plugin — same ` ```mermaid ` syntax |
| YouTube iframe | Standard HTML `<iframe>` — no change needed |

### Images

Co-located images (e.g., `subscriptions/subs-concept/img/`) stay co-located. Starlight supports relative image paths.

## Sidebar Configuration

Defined in `astro.config.mjs`:

```js
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
]
```

Page ordering within groups via `sidebar.order` in frontmatter.

## Integrations

- **Search**: Algolia via `@astrojs/starlight-algolia` (existing credentials, re-crawl after deploy)
- **Mermaid**: `starlight-mermaid` plugin
- **Versioning**: `starlight-versions` plugin — 0.15 archived, current as next
- **Dark/Light mode**: Built into Starlight
- **Header**: Logo, site title, version switcher, social links (GitHub, Discord, Blog, Sponsor)
- **Footer**: Starlight defaults (prev/next navigation)

## What Gets Dropped

- Docusaurus config, React dependencies, Infima CSS framework
- PostHog/Segment analytics plugins
- Custom homepage with hero + feature cards
- Version 0.14 docs
- `.nojekyll` file
- `HomepageFeatures` React component
