export default defineNuxtConfig({
  compatibilityDate: '2026-08-29',
  devtools: { enabled: true },
  ssr: false,
  css: ['~/assets/css/main.css'],
  app: {
    head: {
      title: 'OSkyler',
      meta: [
        { name: 'theme-color', content: '#050612' }
      ],
      link: [
        { rel: 'icon', type: 'image/png', href: '/OSkyler_appicon.png' },
        { rel: 'apple-touch-icon', href: '/OSkyler_appicon.png' },
        { rel: 'manifest', href: '/site.webmanifest' }
      ]
    }
  },
  nitro: {
    output: {
      publicDir: '../wwwroot'
    },
    devProxy: {
      '/api': {
        target: 'http://localhost:5128/api',
        changeOrigin: true
      }
    }
  }
})
