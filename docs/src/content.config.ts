import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';
import { defineCollection } from 'astro:content';
import { docsVersionsLoader } from 'starlight-versions/loader';

export const collections = {
  docs: defineCollection({
    loader: docsLoader({
      generateId({ entry, data }) {
        // If frontmatter has a slug, use it directly (default behavior).
        if (data.slug) return data.slug;
        // Custom ID generation that preserves dots in directory names
        // (e.g. "0.15/intro.mdx" -> "0.15/intro" instead of "015/intro").
        // The default Astro glob loader uses github-slugger which strips dots.
        const withoutExt = entry.replace(/\.\w+$/, '');
        return withoutExt
          .split('/')
          .map(segment => segment.toLowerCase().replace(/\s+/g, '-'))
          .join('/')
          .replace(/\/index$/, '');
      },
    }),
    schema: docsSchema(),
  }),
  versions: defineCollection({ loader: docsVersionsLoader() }),
};
