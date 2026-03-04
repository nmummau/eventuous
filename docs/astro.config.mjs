import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightVersions from 'starlight-versions';
import starlightMermaid from '@pasqal-io/starlight-client-mermaid';

export default defineConfig({
  site: 'https://eventuous.dev',
  integrations: [
    starlight({
      title: 'Eventuous',
      logo: {
        src: './src/assets/logo.png',
      },
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/eventuous/eventuous' },
        { icon: 'discord', label: 'Discord', href: 'https://discord.gg/ZrqM6vnnmf' },
      ],
      customCss: ['./src/styles/custom.css'],
      plugins: [
        starlightVersions({
          current: { label: 'v0.16 (Stable)' },
          versions: [
            { slug: '0.15', label: 'v0.15' },
            { slug: 'next', label: 'Preview' },
          ],
        }),
        starlightMermaid(),
      ],
      sidebar: [
        { label: 'Introduction', slug: 'intro' },
        { label: "What's New", slug: 'whats-new' },
        {
          label: 'Concepts',
          collapsed: true,
          items: [
            { label: 'Prologue', autogenerate: { directory: 'prologue' } },
            { label: 'Domain', autogenerate: { directory: 'domain' } },
            { label: 'Persistence', autogenerate: { directory: 'persistence' } },
          ],
        },
        {
          label: 'Building Apps',
          collapsed: true,
          items: [
            { label: 'Application', autogenerate: { directory: 'application' } },
            { label: 'Subscriptions', autogenerate: { directory: 'subscriptions' } },
            { label: 'Read Models', autogenerate: { directory: 'read-models' } },
            { label: 'Producers', autogenerate: { directory: 'producers' } },
            { label: 'Gateway', autogenerate: { directory: 'gateway' } },
          ],
        },
        {
          label: 'Operations',
          collapsed: true,
          items: [
            { label: 'Diagnostics', autogenerate: { directory: 'diagnostics' } },
            { label: 'Infrastructure', autogenerate: { directory: 'infra' } },
            { label: 'FAQ', autogenerate: { directory: 'faq' } },
          ],
        },
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
