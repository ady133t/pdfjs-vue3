import { createAnnotationStore, uid } from '../lib/annotations'
import type { PageSizeResolver } from '../lib/annotations'
import type { Annotation, AnnotationController, AnnotationTool, NewAnnotation, ViewerExposed } from '../lib/types'

export interface AnnotationPluginOptions {
  /** Default highlight colour. */
  color?: string
  /** Palette shown in the annotation toolbar. */
  colors?: string[]
  /** Name recorded on new annotations. */
  author?: string
  /** Tools exposed in the toolbar. */
  tools?: AnnotationTool[]
  /** Enables localStorage persistence, e.g. `'notes'`. */
  storageKey?: string
  /** Persist after every mutation (default true when storageKey is set). */
  autoSave?: boolean
}

export interface AnnotationControllerApi extends AnnotationController {
  add(input: NewAnnotation): Annotation
}

export interface AnnotationPluginApi {
  name: 'annotation'
  store: ReturnType<typeof createAnnotationStore>
  options: Required<Pick<AnnotationPluginOptions, 'colors' | 'author' | 'tools'>> & AnnotationPluginOptions
  controller: AnnotationControllerApi
  setPageSizeResolver(resolver: PageSizeResolver): void
  /** Restore a previously saved session for this document. */
  restore(docKey: string): number
  persist(docKey: string): void
}

export interface ViewerPlugin {
  name: string
  annotation?: AnnotationPluginApi
}

const DEFAULT_COLORS = ['#ffd54a', '#8ce99a', '#74c0fc', '#ffa8a8', '#d0bfff', '#ffc078']

/**
 * Annotation plugin — the open-source answer to `@vue-pdf-viewer/annotation`.
 * Usage is intentionally identical:
 *
 *   const plugins = [VPdfAnnotationPlugin()]
 *   <PdfViewer :plugins="plugins" />
 */
export function createAnnotationPlugin(options: AnnotationPluginOptions = {}): ViewerPlugin {
  let resolver: PageSizeResolver = () => undefined
  const store = createAnnotationStore((page) => resolver(page))

  store.color.value = options.color ?? '#ffd54a'
  store.author.value = options.author ?? 'Guest'

  const shouldAutoSave = options.autoSave ?? Boolean(options.storageKey)

  const persist = (docKey: string) => {
    if (!options.storageKey) return
    try {
      localStorage.setItem(
        `pdf-viewer:${options.storageKey}:${docKey}`,
        JSON.stringify(store.annotations)
      )
    } catch {
      /* storage full or unavailable */
    }
  }

  const restore = (docKey: string): number => {
    // A plugin instance can outlive a document: never leak annotations
    // (or undo history) from the previous file into the next one.
    store.reset()
    if (!options.storageKey) return 0
    try {
      const raw = localStorage.getItem(`pdf-viewer:${options.storageKey}:${docKey}`)
      if (!raw) return 0
      const parsed = JSON.parse(raw) as Annotation[]
      if (!Array.isArray(parsed)) return 0
      store.annotations.splice(0, store.annotations.length, ...parsed)
      return parsed.length
    } catch {
      return 0
    }
  }

  const controller: AnnotationControllerApi = {
    add: (annotation) => store.add(annotation),
    remove: (id) => store.remove(id),
    update: (id, patch) => store.update(id, patch),
    getAll: () => JSON.parse(JSON.stringify(store.annotations)) as Annotation[],
    clear: () => store.clear(),
    undo: () => store.undo(),
    redo: () => store.redo(),
    canUndo: () => store.canUndo.value,
    canRedo: () => store.canRedo.value,
    select: (id) => store.select(id),
    getSelected: () => store.selectedId.value,
    exportJSON: () => store.exportJSON(),
    importJSON: (json) => store.importJSON(json),
    exportXFDF: () => store.exportXFDF(),
    importXFDF: (xml) => store.importXFDF(xml),
    setTool: (tool) => {
      store.tool.value = tool
    },
    getTool: () => store.tool.value,
    setColor: (color) => {
      store.color.value = color
    },
    getColor: () => store.color.value,
    save: async () => {
      const key = currentDocKey
      if (!key) return
      persist(key)
    },
    load: async () => {
      const key = currentDocKey
      if (!key) return
      restore(key)
    },
  }

  let currentDocKey = ''

  const api: AnnotationPluginApi = {
    name: 'annotation',
    store,
    options: {
      colors: options.colors ?? DEFAULT_COLORS,
      author: options.author ?? 'Guest',
      tools:
        options.tools ??
        ['cursor', 'highlight', 'underline', 'strikeout', 'ink', 'square', 'circle', 'note', 'image'],
      ...options,
    },
    controller,
    setPageSizeResolver: (next) => {
      resolver = next
    },
    restore: (docKey) => {
      currentDocKey = docKey
      return restore(docKey)
    },
    persist: (docKey) => {
      currentDocKey = docKey
      persist(docKey)
    },
  }

  // The viewer watches the annotation list and calls persist() when it changes.
  api.options.autoSave = shouldAutoSave

  return { name: 'annotation', annotation: api }
}

export function createId(): string {
  return uid()
}

export function attachAnnotationController(
  exposed: Partial<ViewerExposed>,
  plugin: ViewerPlugin | undefined
): ViewerExposed | undefined {
  if (!plugin?.annotation) return undefined
  exposed.annotationController = plugin.annotation.controller
  return exposed as ViewerExposed
}

export default createAnnotationPlugin
