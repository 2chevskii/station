import { defineConfig, type DefaultTheme } from 'vitepress'

const englishSidebar: DefaultTheme.Sidebar = [
  {
    text: 'Using Station',
    items: [
      { text: 'Getting started', link: '/guide/getting-started' },
      { text: 'CLI reference', link: '/guide/cli' },
      { text: 'Configuration', link: '/guide/configuration' },
      { text: 'Variables and templates', link: '/guide/variables' },
      { text: 'Roles and tasks', link: '/guide/roles-and-tasks' },
      { text: 'Built-in modules', link: '/guide/modules' },
      { text: 'Plugins', link: '/guide/plugins' },
      { text: 'JavaScript roles', link: '/guide/javascript-roles' },
      { text: 'Troubleshooting', link: '/guide/troubleshooting' },
    ],
  },
  {
    text: 'Contributing',
    items: [
      { text: 'Custom modules', link: '/guide/custom-modules' },
      { text: 'Development', link: '/guide/development' },
    ],
  },
]

const russianSidebar: DefaultTheme.Sidebar = [
  {
    text: 'Использование Station',
    items: [
      { text: 'Быстрый старт', link: '/ru/guide/getting-started' },
      { text: 'Командная строка', link: '/ru/guide/cli' },
      { text: 'Конфигурация', link: '/ru/guide/configuration' },
      { text: 'Переменные и шаблоны', link: '/ru/guide/variables' },
      { text: 'Роли и задачи', link: '/ru/guide/roles-and-tasks' },
      { text: 'Встроенные модули', link: '/ru/guide/modules' },
      { text: 'Плагины', link: '/ru/guide/plugins' },
      { text: 'Роли на JavaScript', link: '/ru/guide/javascript-roles' },
      { text: 'Решение проблем', link: '/ru/guide/troubleshooting' },
    ],
  },
  {
    text: 'Разработка',
    items: [
      { text: 'Собственные модули', link: '/ru/guide/custom-modules' },
      { text: 'Разработка проекта', link: '/ru/guide/development' },
    ],
  },
]

export default defineConfig({
  title: 'Station',
  description: 'Desired-state configuration for a local workstation',
  cleanUrls: true,
  lastUpdated: true,
  themeConfig: {
    search: {
      provider: 'local',
      options: {
        locales: {
          ru: {
            translations: {
              button: {
                buttonText: 'Поиск',
                buttonAriaLabel: 'Поиск по документации',
              },
              modal: {
                noResultsText: 'Ничего не найдено',
              },
            },
          },
        },
      },
    },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/2chevskii/station' },
    ],
  },
  locales: {
    root: {
      label: 'English',
      lang: 'en-US',
      themeConfig: {
        nav: [
          { text: 'Guide', link: '/guide/getting-started' },
          { text: 'Modules', link: '/guide/modules' },
        ],
        sidebar: englishSidebar,
      },
    },
    ru: {
      label: 'Русский',
      lang: 'ru-RU',
      description: 'Декларативная настройка локального компьютера',
      themeConfig: {
        nav: [
          { text: 'Руководство', link: '/ru/guide/getting-started' },
          { text: 'Модули', link: '/ru/guide/modules' },
        ],
        sidebar: russianSidebar,
        outline: { label: 'На этой странице' },
        docFooter: { prev: 'Назад', next: 'Далее' },
        returnToTopLabel: 'Наверх',
        sidebarMenuLabel: 'Меню',
        darkModeSwitchLabel: 'Оформление',
      },
    },
  },
})
