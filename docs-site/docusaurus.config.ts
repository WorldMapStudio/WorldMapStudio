import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'WorldMapStudio',
  tagline: 'Procedural & Non-destructive Map Editor',
  favicon: 'img/favicon.ico',

  future: {
    v4: true,
  },

  url: 'https://WorldMapStudio.github.io',
  baseUrl: '/WorldMapStudio/',

  organizationName: 'WorldMapStudio',
  projectName: 'WorldMapStudio',

  onBrokenLinks: 'throw',

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          editUrl: 'https://github.com/WorldMapStudio/WorldMapStudio/tree/main/docs-site/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    image: 'img/docusaurus-social-card.jpg',
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'WorldMapStudio',
      logo: {
        alt: 'WorldMapStudio logo',
        src: 'img/logo.svg',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'tutorialSidebar',
          position: 'left',
          label: 'Docs',
        },
        {
          href: 'https://github.com/WorldMapStudio/WorldMapStudio',
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Docs',
          items: [
            {
              label: 'Getting Started',
              to: '/docs/intro',
            },
            {
              label: 'CLI',
              to: '/docs/cli',
            },
          ],
        },
        {
          title: 'Project',
          items: [
            {
              label: 'GitHub',
              href: 'https://github.com/WorldMapStudio/WorldMapStudio',
            },
            {
              label: 'npm',
              href: 'https://www.npmjs.com/package/@tsw-project/tsw',
            },
          ],
        },
      ],
      copyright: `Copyright (c) ${new Date().getFullYear()} tsw contributors. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
