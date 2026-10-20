# Open Source PDF Viewer — Vue 3 + TypeScript + SSR

A fully open-source, self-hostable PDF viewer for server-side-rendered Vue 3 apps.
Rendering is handled by **vue-pdf-embed** (MIT) on top of **pdfjs-dist** (Apache-2.0).
No commercial licenses, no watermarks, no telemetry, no external service calls except
the PDF you point it at.

The whole thing is MIT licensed — fork it, rebrand it, ship it, sell it, tear it apart.

## Table of Contents

- [Stack](#stack)
- [Installation](#installation)
- [Running](#running)
- [What's Included](#whats-included)
- [How SSR Is Handled](#how-ssr-is-handled)
- [Project Layout](#project-layout)
- [License](#license)

## Stack

| Layer      | Choice                          | License    |
| ---------- | ------------------------------- | ---------- |
| Framework  | Vue 3 (`createSSRApp`)          | MIT        |
| Language   | TypeScript                      | Apache-2.0 |
| Build      | Vite 6                          | MIT        |
| Server     | Express 4                       | MIT        |
| PDF render | `vue-pdf-embed` → `pdfjs-dist`  | MIT / Apache-2.0 |

## Installation

```bash
npm install
# or
yarn install
# or
pnpm install
```

Requires Node.js 18 or newer (uses top-level `await` in `server.js`).

## Running

```bash
# Development server (Express + Vite middleware, HMR)
npm run dev

# Production build (client + server bundles)
npm run build

# Preview the production build
npm run preview

# Type-check the whole project
npm run typecheck
```

Open `http://localhost:5173` in your browser. Override the port with `PORT`, or host
under a sub-path with `BASE=/subdir/`.

## What's Included

`src/App.vue` demonstrates three configurations of the same viewer:

- **Default Toolbar** — page navigation, zoom in/out/reset
- **Without Toolbar** — bare render via `showToolbar: false`
- **Mobile** — fixed 468px-wide viewport

The reusable viewer lives in `src/components/PdfViewer.vue`. Props:

| Prop          | Type      | Default | Description                         |
| ------------- | --------- | ------- | ----------------------------------- |
| `source`      | `string`  | —       | URL, `File`, `Uint8Array` or base64 |
| `width`       | `number`  | `980`   | Render width in px (zoom scales it) |
| `showToolbar` | `boolean` | `true`  | Toggle the built-in toolbar         |

The parent controls width; the toolbar multiplies it by the current zoom factor
(clamped to 50%–300%).

## How SSR Is Handled

`pdfjs-dist` touches browser-only APIs (`document`, `canvas`, `Worker`), so the viewer
must never render on the server. Two guards keep the SSR pass clean:

1. `defineAsyncComponent` resolves to a `Loading...` placeholder when
   `import.meta.env.SSR` is true, and imports the real component only on the client.
2. `src/components/ClientOnly.vue` renders its slot only after `onMounted`, so nothing
   canvas-shaped ever reaches `renderToString`.

`vite.config.ts` marks `vue-pdf-embed` and `pdfjs-dist` as `ssr.noExternal` so Vite
bundles them for the client build instead of leaving them as Node externals.

The server returns real hydrated HTML: headings, copy and layout are in the initial
response for crawlers and slow links, and the viewer mounts onto it on the client.

## Project Layout

```
.
├── index.html                 # SSR template with <!--app-head--> / <!--app-html-->
├── server.js                  # Express: Vite middleware in dev, sirv + compression in prod
├── vite.config.ts             # Vue plugin + SSR externals
├── src
│   ├── main.ts                # createApp() factory (fresh instance per request)
│   ├── entry-client.ts        # hydrate/mount on the client
│   ├── entry-server.ts        # renderToString for the server
│   ├── App.vue                # three demo configurations
│   ├── style.css              # global styles
│   └── components
│       ├── PdfViewer.vue      # the reusable viewer + toolbar
│       └── ClientOnly.vue     # mount-gated slot
└── public                     # static assets
```

## Self-hosting the PDF.js worker

By default `vue-pdf-embed` resolves the worker through the bundled `pdfjs-dist` build.
If you serve the app under a strict Content-Security-Policy or from a sub-path, copy
`node_modules/pdfjs-dist/build/pdf.worker.min.mjs` into `public/` and point the worker
at it in `PdfViewer.vue`:

```ts
import * as pdfjsLib from 'pdfjs-dist'
pdfjsLib.GlobalWorkerOptions.workerSrc = '/pdf.worker.min.mjs'
```

That keeps every asset on your own origin — useful for air-gapped deployments.

## License

MIT — see [LICENSE](./LICENSE). Copyright (c) 2026 the contributors.

Third-party notices:

- `vue-pdf-embed` — MIT
- `pdfjs-dist` — Apache-2.0
- `vue` — MIT
