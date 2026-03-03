import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';
import { defineCollection } from 'astro:content';
// TODO: Re-enable after migrating 0.15 content (Task 5)
// import { docsVersionsLoader } from 'starlight-versions/loader';

export const collections = {
  docs: defineCollection({ loader: docsLoader(), schema: docsSchema() }),
  // TODO: Re-enable after migrating 0.15 content (Task 5)
  // versions: defineCollection({ loader: docsVersionsLoader() }),
};
