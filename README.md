# Open Source PDF Viewer — Vue 3 + TypeScript + SSR

A complete, self-hosted PDF viewer for Vue 3 apps, including server-side rendering.
It is built directly on **pdfjs-dist** and ships with:

- canvas rendering with a selectable text layer
- continuous scroll and single-page modes
- page navigation, zoom, fit-width / fit-page, rotation
- full-document search with match highlighting
- thumbnail and outline sidebars
- annotation tools: highlight, underline, strike-out, ink, rectangle, circle, notes, images
- annotation undo/redo, JSON export/import and XFDF export/import
- download, print, full screen, dark mode, document properties
- password-protected PDFs
- keyboard shortcuts and drag-and-drop / file picker loading
- a plugin + controller API modelled after popular Vue PDF viewer starters

Everything is MIT licensed. There are no license keys, watermarks, telemetry, or
calls to third-party services — only the PDF you point the viewer at.

> This is an independent open-source implementation. Some prop and event names
> intentionally match commercial `@vue-pdf-viewer` starters so existing code can
> migrate easily, but none of their code is included or required.

## Table of Contents

- [Stack](#stack)
- [Installation](#installation)
- [Running](#running)
- [Quick Start](#quick-start)
- [Component API](#component-api)
  - [Props](#props)
  - [Events](#events)
  - [Exposed controllers](#exposed-controllers)
- [Toolbar](#toolbar)
- [Annotations](#annotations)
- [SSR](#ssr)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Project layout](#project-layout)
- [License](#license)

## Stack

| Layer      | Choice                     | License    |
| ---------- | -------------------------- | ---------- |
| Framework  | Vue 3 (`createSSRApp`)     | MIT        |
| Language   | TypeScript                 | Apache-2.0 |
| Build      | Vite 6                     | MIT        |
| Server     | Express 4 + sirv           | MIT        |
| PDF engine | `pdfjs-dist` 6             | Apache-2.0 |

## Installation

```bash
npm install
```

Requires Node.js 22.13 or newer (`pdfjs-dist` 6 and the top-level `await` in
`server.js`).

During `npm run dev` and `npm run build`, a small script copies pdf.js runtime
assets (`cmaps`, `standard_fonts`, `wasm`) from `node_modules/pdfjs-dist` into
`public/pdfjs`. They are generated files and are not committed.

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

Open `http://localhost:5173`. Override the port with `PORT` and host under a
sub-path with `BASE=/subdir/`.

## Quick Start

`src/App.vue` contains five live demos. The smallest possible usage:

```vue
<script setup lang="ts">
import { defineAsyncComponent } from "vue";
import ClientOnly from "./components/ClientOnly.vue";

const PdfViewer = defineAsyncComponent(() => import("./components/PdfViewer.vue"));
</script>

<template>
  <ClientOnly>
    <PdfViewer src="/sample.pdf" :height="720" />
  </ClientOnly>
</template>
```

The viewer fills its parent element. Pass `height` when the parent has no
explicit height.

## Component API

### Props

| Prop             | Type                                          | Default        | Description                                                                 |
| ---------------- | --------------------------------------------- | -------------- | --------------------------------------------------------------------------- |
| `src`            | `string \| Uint8Array \| ArrayBuffer`         | —              | PDF URL or raw bytes. `source` is accepted as an alias.                     |
| `source`         | same as `src`                                 | —              | Alias for `src`, kept for drop-in compatibility.                            |
| `plugins`        | `ViewerPlugin[]`                              | `[]`           | Plugin list, e.g. `[createAnnotationPlugin()]`.                             |
| `toolbarOptions` | `ToolbarOptions \| false`                     | `{}`           | Toolbar configuration. `false` hides the toolbar completely.                |
| `initialPage`    | `number`                                      | `1`            | Page shown after loading.                                                   |
| `initialScale`   | `number \| "auto" \| "page-fit"`              | `"auto"`       | `"auto"` fits the page width, `"page-fit"` fits the whole page.             |
| `theme`          | `"light" \| "dark" \| "auto"`                 | `"auto"`       | Initial colour theme. The toolbar toggle updates the local theme.           |
| `height`         | `string \| number`                            | `"100%"`       | CSS height, or a pixel number.                                              |
| `scrollMode`     | `"continuous" \| "single"`                    | `"continuous"` | Scroll all pages, or render one page at a time.                             |
| `filename`       | `string`                                      | —              | Download filename. Defaults to the URL basename or `document.pdf`.          |

### Events

| Event             | Payload                            |
| ----------------- | ---------------------------------- |
| `loaded`          | `PDFDocumentProxy`                 |
| `loadProgress`    | `{ loaded: number; total: number }`|
| `loadError`       | `unknown`                          |
| `pageChanged`     | `number`                           |
| `rotate`          | `number` (degrees)                 |
| `scroll`          | `{ page: number; top: number }`    |
| `update:darkMode` | `boolean`                          |
| `passwordRequired`| `number` (pdf.js password reason)  |
| `passwordSuccess` | —                                  |
| `passwordError`   | `number`                           |

```vue
<PdfViewer
  :src="url"
  @loaded="onLoaded"
  @pageChanged="page = $event"
  @update:darkMode="dark = $event" />
```

### Exposed controllers

```vue
<script setup lang="ts">
import { ref } from "vue";
const viewer = ref<InstanceType<typeof PdfViewer> | null>(null);

function jump() {
  viewer.value?.pageController.goToPage(3);
  viewer.value?.zoomController.setZoom(1.5);
  viewer.value?.searchController.search("invoice");
}
</script>

<template>
  <PdfViewer ref="viewer" src="/sample.pdf" />
</template>
```

| Controller             | Methods                                                                                                   |
| ---------------------- | --------------------------------------------------------------------------------------------------------- |
| `pageController`       | `goToPage`, `nextPage`, `previousPage`, `getPage`, `getPageCount`                                          |
| `zoomController`       | `zoomIn`, `zoomOut`, `setZoom`, `getZoom`, `fitWidth`, `fitPage`                                           |
| `rotateController`     | `rotateClockwise`, `rotateCounterClockwise`, `setRotation`, `getRotation`                                  |
| `searchController`     | `search`, `next`, `previous`, `clear`, `getResults`                                                        |
| `downloadController`   | `download`                                                                                                 |
| `printController`      | `print`                                                                                                    |
| `annotationController` | `add`, `remove`, `update`, `getAll`, `clear`, `undo`, `redo`, `exportJSON`, `importJSON`, `exportXFDF`, `importXFDF`, `setTool`, `save`, `load` |
| `getDocument`          | Returns the current `PDFDocumentProxy`.                                                                     |

## Toolbar

`toolbarOptions` can hide the toolbar or select and reorder its items:

```ts
import type { ToolbarOptions } from "./lib/types";

const toolbarOptions: ToolbarOptions = {
  items: [
    "thumbnails",
    "outline",
    "open",
    "pageNav",
    "zoom",
    "rotate",
    "search",
    "annotations",
    "download",
    "print",
    "fullscreen",
    "theme",
    "properties",
  ],
};

// Hide entirely:
const minimal: ToolbarOptions = { show: false };
```

Any item can also be disabled individually, e.g. `{ download: false }`.

Items: `open`, `download`, `print`, `search`, `pageNav`, `zoom`, `rotate`,
`thumbnails`, `outline`, `annotations`, `fullscreen`, `theme`, `properties`.

## Annotations

The annotation plugin is included and mirrors the ergonomics of
`@vue-pdf-viewer/annotation`:

```ts
import createAnnotationPlugin from "./plugins/annotation";

const plugins = [
  createAnnotationPlugin({
    author: "Ada Lovelace",
    color: "#ffd54a",
    storageKey: "my-app", // optional localStorage persistence
  }),
];
```

```vue
<PdfViewer :src="url" :plugins="plugins" :height="720" />
```

Plugin options:

| Option       | Type                 | Default                | Description                                              |
| ------------ | -------------------- | ---------------------- | -------------------------------------------------------- |
| `color`      | `string`             | `"#ffd54a"`            | Initial annotation colour.                               |
| `colors`     | `string[]`           | six-colour palette     | Swatches shown in the annotation toolbar.                |
| `author`     | `string`             | `"Guest"`              | Author recorded on new annotations.                      |
| `tools`      | `AnnotationTool[]`   | all tools              | Subset shown in the second toolbar row.                   |
| `storageKey` | `string`             | —                      | Namespace for per-document `localStorage` persistence.   |
| `autoSave`   | `boolean`            | `true` with storageKey | Persist after every annotation change.                   |

Available tools: `cursor`, `highlight`, `underline`, `strikeout`, `ink`, `square`,
`circle`, `note`, `image`.

- Markup annotations use the browser text selection.
- Annotation geometry is stored relative to the page's unrotated display box, so
  it survives zoom, responsive resizing, rotation and a full reload. Exporting
  after rotating the viewer does not bake the view rotation into the file.
- JSON is the lossless format. XFDF supports the common subset used by Adobe
  readers: highlight, underline, strikeout, ink, square, circle, free text and
  text notes.
- `storageKey` persists annotations per document in `localStorage`; the viewer
  restores them automatically.
- Printing rasterises pages at 1.6× for reliable pagination; text stays
  selectable in the viewer, not in the printed copy.

Programmatic access:

```ts
const controller = viewer.value?.annotationController;
controller?.setTool("highlight");
const json = controller?.exportJSON();
controller?.importXFDF(xfdfString);
```

## SSR

The viewer talks to canvas, workers and `IntersectionObserver`, so it is mounted
client-side only. The entry files (`entry-client.ts`, `entry-server.ts`) and the
`ClientOnly` wrapper keep the server render safe. `src` is not fetched on the
server; the first HTML response contains the page shell, and hydration loads the
document.

pdf.js runs in a real Web Worker. Its runtime assets are served from the app's
own origin at `<base>/pdfjs/...`; `BASE` from the environment is applied
automatically, so sub-path deployments work without further configuration.

## Keyboard shortcuts

Shortcuts apply to the viewer that currently has focus (click the page or toolbar
first):

| Keys                        | Action                    |
| --------------------------- | ------------------------- |
| `→` / `PageDown`            | Next page                 |
| `←` / `PageUp`              | Previous page             |
| `+` / `-`                   | Zoom in / out             |
| `0`                         | Reset zoom to 100%        |
| `Home` / `End`              | First / last page         |
| `Ctrl`/`Cmd` + `F`          | Focus search              |
| `Ctrl`/`Cmd` + `Z`          | Undo annotation           |
| `Ctrl`/`Cmd` + `Shift` + `Z`| Redo annotation           |
| `Delete`                    | Delete selected annotation|
| `Esc`                       | Close panels/dialogs      |

## Project layout

```text
server.js                     Express + Vite dev middleware / sirv in production
scripts/copy-pdfjs-assets.mjs copies cmaps, fonts and wasm into public/pdfjs
src/
  App.vue                     Five demo configurations
  components/
    PdfViewer.vue             Main viewer, document loading, search, controllers
    PdfPage.vue               Canvas, text layer, links, annotation editing
    PdfToolbar.vue            Toolbar and annotation tool row
    PdfSidebar.vue            Thumbnails, outline, annotation list
    AnnotationView.vue        Renders one annotation
    PdfOutline.vue            Recursive outline tree
    PdfPropertiesDialog.vue   Document metadata dialog
    PdfPasswordDialog.vue     Password prompt
    ClientOnly.vue            SSR-safe client mount helper
    PdfViewerLicense.vue      No-op compatibility wrapper (no licence checks)
  lib/
    types.ts                  Public types and controller interfaces
    annotations.ts            Annotation store, undo/redo, JSON and XFDF
    pdf-geometry.ts           Viewport helpers
  plugins/annotation.ts       createAnnotationPlugin()
  styles/viewer.css           Viewer theme tokens and pdf.js text layer CSS
```

## License

MIT — see [LICENSE](./LICENSE). `pdfjs-dist` is Apache-2.0; Vue, Vite and Express
are MIT.
