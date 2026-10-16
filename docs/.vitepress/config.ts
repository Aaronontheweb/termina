import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Termina',
  description: 'Reactive Terminal UI Framework for .NET',
  base: '/termina/',

  head: [
    ['link', { rel: 'icon', href: '/termina/termina-icon.png' }]
  ],

  themeConfig: {
    logo: '/termina-icon.png',

    nav: [
      { text: 'Guide', link: '/guide/' },
      { text: 'Tutorials', link: '/tutorials/' },
      { text: 'Layout', link: '/layout/' },
      { text: 'Components', link: '/components/' },
      { text: 'Styling', link: '/styling/' },
      { text: 'Concepts', link: '/concepts/' },
      { text: 'Advanced', link: '/advanced/' },
      { text: 'Analyzers', link: '/analyzers/' },
      { text: 'Changelog', link: '/changelog' }
    ],

    sidebar: {
      '/guide/': [
        { text: 'Introduction', link: '/guide/' },
        { text: 'Getting Started', link: '/guide/getting-started' },
        { text: 'Installation', link: '/guide/installation' },
        { text: 'Render Loop Threading', link: '/guide/upgrade-render-loop-threading' },
        { text: 'Upgrading to 0.11.0', link: '/guide/upgrade-0.11' },
        { text: 'Migrating to 0.7.0', link: '/guide/migration-0.7' }
      ],
      '/tutorials/': [
        { text: 'Overview', link: '/tutorials/' },
        { text: 'Counter App', link: '/tutorials/counter-app' },
        { text: 'Todo List', link: '/tutorials/todo-list' },
        { text: 'Streaming Chat', link: '/tutorials/streaming-chat' },
        { text: 'Setup Wizard', link: '/tutorials/wizard-app' }
      ],
      '/layout/': [
        { text: 'Layout System', link: '/layout/' },
        { text: 'Size Constraints', link: '/layout/size-constraints' },
        { text: 'Vertical & Horizontal', link: '/layout/vertical-horizontal' },
        { text: 'Grid Layout', link: '/layout/grid' },
        { text: 'Nesting Layouts', link: '/layout/nesting' },
        { text: 'Responsive Design', link: '/layout/responsive' }
      ],
      '/components/': [
        { text: 'Component Library', link: '/components/' },
        {
          text: 'Display Components',
          collapsed: false,
          items: [
            { text: 'TextNode', link: '/components/text-node' },
            { text: 'StreamingTextNode', link: '/components/streaming-text-node' },
            { text: 'PanelNode', link: '/components/panel-node' },
            { text: 'SpinnerNode', link: '/components/spinner-node' }
          ]
        },
        {
          text: 'Input Components',
          collapsed: false,
          items: [
            { text: 'TextInputNode', link: '/components/text-input-node' },
            { text: 'TextAreaNode', link: '/components/text-area-node' },
            { text: 'CopyableTextNode', link: '/components/copyable-text-node' },
            { text: 'SelectionListNode', link: '/components/selection-list-node' },
            { text: 'FilePickerNode', link: '/components/file-picker-node' }
          ]
        },
        {
          text: 'Container Components',
          collapsed: false,
          items: [
            { text: 'ScrollableContainer', link: '/components/scrollable-container' },
            { text: 'StackLayout', link: '/components/stack-layout' },
            { text: 'ModalNode', link: '/components/modal-node' }
          ]
        },
        {
          text: 'Reactive Components',
          collapsed: false,
          items: [
            { text: 'ReactiveLayoutNode', link: '/components/reactive-layout' },
            { text: 'ConditionalNode', link: '/components/conditional-node' }
          ]
        },
        {
          text: 'Utility Components',
          collapsed: false,
          items: [
            { text: 'EmptyNode', link: '/components/empty-node' },
            { text: 'DeferredNode', link: '/components/deferred-node' }
          ]
        }
      ],
      '/styling/': [
        { text: 'Styling Guide', link: '/styling/' },
        { text: 'Colors', link: '/styling/colors' },
        { text: 'Text Formatting', link: '/styling/text-formatting' },
        { text: 'Borders', link: '/styling/borders' },
        { text: 'Theming', link: '/styling/theming' }
      ],
      '/concepts/': [
        { text: 'Core Concepts', link: '/concepts/' },
        { text: 'Architecture', link: '/concepts/architecture' },
        { text: 'Reactive Properties', link: '/concepts/reactive-properties' },
        { text: 'Observables & Rx', link: '/concepts/observables' },
        { text: 'Render Loop Threading', link: '/concepts/render-loop-threading' },
        { text: 'Source Generators', link: '/concepts/source-generators' },
        { text: 'Routing', link: '/concepts/routing' },
        { text: 'Navigation', link: '/concepts/navigation' },
        { text: 'Input Handling', link: '/concepts/input-handling' },
        { text: 'Unicode Terminal Width', link: '/concepts/unicode-terminal-width' },
        { text: 'Presentation Modes', link: '/concepts/presentation-modes' },
        { text: 'Terminal Input Modes', link: '/concepts/terminal-input-modes' },
        { text: 'tmux Configuration', link: '/concepts/tmux-configuration' },
        { text: 'Dynamic Layouts', link: '/concepts/dynamic-layouts' },
        { text: 'Focus Management', link: '/concepts/focus-management' },
        { text: 'Hosting & DI', link: '/concepts/hosting' }
      ],
      '/advanced/': [
        { text: 'Advanced Topics', link: '/advanced/' },
        { text: 'Testing', link: '/advanced/testing' },
        { text: 'Diagnostics', link: '/advanced/diagnostics' },
        { text: 'Custom Components', link: '/advanced/custom-components' },
        { text: 'AOT Compilation', link: '/advanced/aot' },
        { text: 'Akka.NET Integration', link: '/advanced/akka-integration' }
      ],
      '/analyzers/': [
        { text: 'Analyzer Reference', link: '/analyzers/' },
        { text: 'TERMINA002: Layout Nodes in ViewModels', link: '/analyzers/termina002' },
        { text: 'TERMINA003: Child Disposal', link: '/analyzers/termina003' },
        { text: 'TERMINA004: Stateful Node Recreation', link: '/analyzers/termina004' }
      ],
      '/comparison/': [
        { text: 'Comparisons', link: '/comparison/' },
        { text: 'vs Spectre.Console', link: '/comparison/vs-spectre-console' },
        { text: 'vs Terminal.Gui', link: '/comparison/vs-terminal-gui' }
      ]
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/Aaronontheweb/Termina' }
    ],

    editLink: {
      pattern: 'https://github.com/Aaronontheweb/Termina/edit/dev/docs/:path'
    },

    search: {
      provider: 'local'
    },

    footer: {
      message: 'Released under the Apache 2.0 License.',
      copyright: 'Copyright © 2025 <a href="https://aaronstannard.com/">Aaron Stannard</a>'
    }
  },

  markdown: {
    lineNumbers: true
  }
})
