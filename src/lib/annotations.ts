import { computed, reactive, ref } from 'vue'
import type {
  Annotation,
  AnnotationTool,
  FreeTextAnnotation,
  ImageAnnotation,
  InkAnnotation,
  Point,
  Rect,
  ShapeAnnotation,
  TextMarkupAnnotation,
  NewAnnotation,
} from './types'

export interface PageSize {
  width: number
  height: number
}

export type PageSizeResolver = (page: number) => PageSize | undefined

let counter = 0

export function uid(prefix = 'ann'): string {
  counter += 1
  return `${prefix}-${Date.now().toString(36)}-${counter.toString(36)}`
}

function round(value: number): number {
  return Math.round(value * 10000) / 10000
}

function normalizeRect(rect: Rect): Rect {
  const x = Math.min(rect.x, rect.x + rect.w)
  const y = Math.min(rect.y, rect.y + rect.h)
  return {
    x: round(x),
    y: round(y),
    w: round(Math.abs(rect.w)),
    h: round(Math.abs(rect.h)),
  }
}

function escapeXml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;')
}

function hexToPdfColor(hex: string): string {
  const clean = hex.replace('#', '')
  const full = clean.length === 3 ? clean.split('').map((c) => c + c).join('') : clean
  const r = parseInt(full.slice(0, 2), 16) / 255
  const g = parseInt(full.slice(2, 4), 16) / 255
  const b = parseInt(full.slice(4, 6), 16) / 255
  return [r, g, b].map((n) => (Number.isFinite(n) ? n.toFixed(3) : '0.000')).join(' ')
}

function parsePdfColor(value: string | null, fallback: string): string {
  if (!value) return fallback
  const parts = value.trim().split(/\s+/).map(Number)
  if (parts.length < 3 || parts.some((n) => !Number.isFinite(n))) return fallback
  const toHex = (n: number) =>
    Math.max(0, Math.min(255, Math.round(n * 255)))
      .toString(16)
      .padStart(2, '0')
  return `#${toHex(parts[0])}${toHex(parts[1])}${toHex(parts[2])}`
}

/**
 * Reactive annotation store with undo/redo and PDF-friendly serialization.
 * Coordinates are stored normalized (0..1) against the displayed page box so
 * they survive zoom, rotation changes and responsive resizing.
 */
export function createAnnotationStore(getPageSize: PageSizeResolver) {
  const annotations = reactive<Annotation[]>([])
  const selectedId = ref<string | null>(null)
  const tool = ref<AnnotationTool>('cursor')
  const color = ref('#ffd54a')
  const strokeWidth = ref(2)
  const fontSize = ref(14)
  const author = ref('Guest')

  const undoStack = ref<string[]>([])
  const redoStack = ref<string[]>([])
  const maxHistory = 100

  function snapshot(): string {
    return JSON.stringify(annotations)
  }

  function pushHistory(): void {
    undoStack.value.push(snapshot())
    if (undoStack.value.length > maxHistory) undoStack.value.shift()
    redoStack.value.length = 0
  }

  function restore(json: string): void {
    const parsed = JSON.parse(json) as Annotation[]
    annotations.splice(0, annotations.length, ...parsed)
    if (selectedId.value && !annotations.some((a) => a.id === selectedId.value)) {
      selectedId.value = null
    }
  }

  function add(input: NewAnnotation): Annotation {
    pushHistory()
    const base = {
      id: input.id ?? uid(),
      page: input.page,
      color: input.color,
      createdAt: new Date().toISOString(),
      author: input.author ?? author.value,
    }
    const annotation = { ...input, ...base } as Annotation
    annotations.push(annotation)
    selectedId.value = annotation.id
    return annotation
  }

  function remove(id: string): void {
    const index = annotations.findIndex((a) => a.id === id)
    if (index === -1) return
    pushHistory()
    annotations.splice(index, 1)
    if (selectedId.value === id) selectedId.value = null
  }

  function update(id: string, patch: Partial<Annotation>): void {
    const index = annotations.findIndex((a) => a.id === id)
    if (index === -1) return
    pushHistory()
    annotations[index] = {
      ...annotations[index],
      ...patch,
      id,
      updatedAt: new Date().toISOString(),
    } as Annotation
  }

  function clear(): void {
    if (!annotations.length) return
    pushHistory()
    annotations.splice(0, annotations.length)
    selectedId.value = null
  }

  /** Drop annotations and history, e.g. when another document is loaded. */
  function reset(): void {
    annotations.splice(0, annotations.length)
    selectedId.value = null
    undoStack.value.length = 0
    redoStack.value.length = 0
  }

  function undo(): void {
    const previous = undoStack.value.pop()
    if (previous === undefined) return
    redoStack.value.push(snapshot())
    restore(previous)
  }

  function redo(): void {
    const next = redoStack.value.pop()
    if (next === undefined) return
    undoStack.value.push(snapshot())
    restore(next)
  }

  function byPage(page: number): Annotation[] {
    return annotations.filter((a) => a.page === page)
  }

  function select(id: string | null): void {
    selectedId.value = id
  }

  function exportJSON(): string {
    return JSON.stringify(
      {
        version: 1,
        generator: 'open-pdf-viewer',
        exportedAt: new Date().toISOString(),
        annotations,
      },
      null,
      2
    )
  }

  function importJSON(json: string): number {
    const parsed = JSON.parse(json) as Annotation[] | { annotations?: Annotation[] }
    const list = Array.isArray(parsed) ? parsed : parsed.annotations
    if (!Array.isArray(list)) throw new Error('Unsupported annotation file')
    pushHistory()
    let imported = 0
    for (const item of list) {
      if (!item || typeof item.page !== 'number' || !item.type) continue
      annotations.push({
        ...item,
        id: item.id ?? uid(),
        author: item.author ?? author.value,
        createdAt: item.createdAt ?? new Date().toISOString(),
      } as Annotation)
      imported += 1
    }
    return imported
  }

  function formatDate(value?: string): string {
    if (!value) return ''
    const date = new Date(value)
    if (Number.isNaN(date.getTime())) return ''
    return date.toISOString()
  }

  function exportXFDF(): string {
    const items: string[] = []
    for (const annotation of annotations) {
      const size = getPageSize(annotation.page)
      if (!size) continue
      const page = annotation.page - 1
      const common = `page="${page}" color="${hexToPdfColor(annotation.color)}" date="${formatDate(
        annotation.createdAt
      )}" subject="${escapeXml(annotation.type)}"`

      if (annotation.type === 'highlight' || annotation.type === 'underline' || annotation.type === 'strikeout') {
        const markup = annotation as TextMarkupAnnotation
        const vertices = markup.rects
          .flatMap((rect) => {
            const r = normalizeRect(rect)
            const x1 = r.x * size.width
            const y1 = (1 - r.y - r.h) * size.height
            const x2 = (r.x + r.w) * size.width
            const y2 = (1 - r.y) * size.height
            return [x1, y1, x1, y2, x2, y2, x2, y1]
          })
          .map((n) => n.toFixed(2))
          .join(',')
        items.push(
          `<${markup.type} ${common} name="${markup.id}" flags="print" contents="${escapeXml(
            markup.text ?? ''
          )}"><vertices>${vertices}</vertices></${markup.type}>`
        )
        continue
      }

      if (annotation.type === 'note') {
        const note = annotation as FreeTextAnnotation
        const r = normalizeRect(note.rect)
        const rect = [
          (r.x * size.width).toFixed(2),
          ((1 - r.y - r.h) * size.height).toFixed(2),
          ((r.x + r.w) * size.width).toFixed(2),
          ((1 - r.y) * size.height).toFixed(2),
        ].join(',')
        items.push(
          `<freetext ${common} name="${note.id}" rect="${rect}" fontSize="${note.fontSize}"><contents>${escapeXml(
            note.text
          )}</contents></freetext>`
        )
        continue
      }

      if (annotation.type === 'square' || annotation.type === 'circle') {
        const shape = annotation as ShapeAnnotation
        const r = normalizeRect(shape.rect)
        const rect = [
          (r.x * size.width).toFixed(2),
          ((1 - r.y - r.h) * size.height).toFixed(2),
          ((r.x + r.w) * size.width).toFixed(2),
          ((1 - r.y) * size.height).toFixed(2),
        ].join(',')
        items.push(
          `<${shape.type} ${common} name="${shape.id}" rect="${rect}" width="${shape.width}" />`
        )
        continue
      }

      if (annotation.type === 'ink') {
        const ink = annotation as InkAnnotation
        const inkList = ink.paths
          .map((path) =>
            path
              .map(
                (point) =>
                  `${(point.x * size.width).toFixed(2)},${((1 - point.y) * size.height).toFixed(2)}`
              )
              .join(',')
          )
          .map((path) => `<inkList><gesture>${path}</gesture></inkList>`)
          .join('')
        items.push(`<ink ${common} name="${ink.id}">${inkList}</ink>`)
      }
    }

    return `<?xml version="1.0" encoding="UTF-8"?>
<xfdf xmlns="http://ns.adobe.com/xfdf/" xml:space="preserve">
<fields />
<annots>
${items.join('\n')}
</annots>
</xfdf>`
  }

  function importXFDF(xml: string): number {
    const doc = new DOMParser().parseFromString(xml, 'application/xml')
    if (doc.querySelector('parsererror')) throw new Error('Invalid XFDF document')
    pushHistory()
    let imported = 0

    const readRect = (element: Element): Rect | null => {
      const raw = element.getAttribute('rect')
      if (!raw) return null
      const [x1, y1, x2, y2] = raw.split(',').map(Number)
      if ([x1, y1, x2, y2].some((n) => !Number.isFinite(n))) return null
      const size = getPageSize(Number(element.getAttribute('page')) + 1)
      if (!size) return null
      return {
        x: x1 / size.width,
        y: 1 - y2 / size.height,
        w: (x2 - x1) / size.width,
        h: (y2 - y1) / size.height,
      }
    }

    for (const element of Array.from(doc.querySelectorAll('annots > *'))) {
      const tag = element.tagName.toLowerCase()
      const page = Number(element.getAttribute('page') ?? '-1') + 1
      if (!page || page < 1) continue
      const annotationColor = parsePdfColor(element.getAttribute('color'), color.value)

      if (tag === 'highlight' || tag === 'underline' || tag === 'strikeout') {
        const raw = element.querySelector('vertices')?.textContent ?? ''
        const numbers = raw
          .split(',')
          .map(Number)
          .filter((n) => Number.isFinite(n))
        const size = getPageSize(page)
        if (!size || numbers.length < 8) continue
        const rects: Rect[] = []
        const addRect = (x1: number, y1: number, x2: number, y2: number) => {
          const left = Math.min(x1, x2)
          const right = Math.max(x1, x2)
          const top = Math.max(y1, y2)
          const bottom = Math.min(y1, y2)
          rects.push({
            x: left / size.width,
            y: 1 - top / size.height,
            w: (right - left) / size.width,
            h: (top - bottom) / size.height,
          })
        }
        for (let i = 0; i + 7 < numbers.length; i += 8) {
          addRect(numbers[i], numbers[i + 1], numbers[i + 6], numbers[i + 7])
        }
        annotations.push({
          id: uid(),
          page,
          type: tag,
          color: annotationColor,
          author: author.value,
          createdAt: new Date().toISOString(),
          rects,
          text: element.getAttribute('contents') ?? '',
        } as TextMarkupAnnotation)
        imported += 1
        continue
      }

      if (tag === 'ink') {
        const size = getPageSize(page)
        if (!size) continue
        const paths: Point[][] = Array.from(element.querySelectorAll('gesture')).map((gesture) =>
          (gesture.textContent ?? '')
            .split(',')
            .map(Number)
            .reduce<Point[]>((points, value, index, all) => {
              if (index % 2 === 0 && Number.isFinite(value) && Number.isFinite(all[index + 1])) {
                points.push({
                  x: value / size.width,
                  y: 1 - all[index + 1] / size.height,
                })
              }
              return points
            }, [])
        )
        if (!paths.length) continue
        annotations.push({
          id: uid(),
          page,
          type: 'ink',
          color: annotationColor,
          author: author.value,
          createdAt: new Date().toISOString(),
          paths,
          width: strokeWidth.value,
        } as InkAnnotation)
        imported += 1
        continue
      }

      const rect = readRect(element)
      if (!rect) continue

      if (tag === 'freetext') {
        annotations.push({
          id: uid(),
          page,
          type: 'note',
          color: annotationColor,
          author: author.value,
          createdAt: new Date().toISOString(),
          rect: normalizeRect(rect),
          text: element.querySelector('contents')?.textContent ?? '',
          fontSize: Number(element.getAttribute('fontSize') ?? 14),
        } as FreeTextAnnotation)
        imported += 1
        continue
      }

      if (tag === 'square' || tag === 'circle') {
        annotations.push({
          id: uid(),
          page,
          type: tag,
          color: annotationColor,
          author: author.value,
          createdAt: new Date().toISOString(),
          rect: normalizeRect(rect),
          width: Number(element.getAttribute('width') ?? strokeWidth.value),
        } as ShapeAnnotation)
        imported += 1
        continue
      }

      if (tag === 'text') {
        annotations.push({
          id: uid(),
          page,
          type: 'note',
          color: annotationColor,
          author: author.value,
          createdAt: new Date().toISOString(),
          rect: normalizeRect(rect),
          text: element.querySelector('contents')?.textContent ?? '',
          fontSize: fontSize.value,
        } as FreeTextAnnotation)
        imported += 1
        continue
      }

      if (tag === 'image') {
        annotations.push({
          id: uid(),
          page,
          type: 'image',
          color: annotationColor,
          author: author.value,
          createdAt: new Date().toISOString(),
          rect: normalizeRect(rect),
          src: element.querySelector('contents')?.textContent ?? '',
        } as ImageAnnotation)
        imported += 1
      }
    }

    if (!imported) {
      undoStack.value.pop()
    }
    return imported
  }

  const countByPage = computed(() => {
    const map = new Map<number, number>()
    for (const annotation of annotations) {
      map.set(annotation.page, (map.get(annotation.page) ?? 0) + 1)
    }
    return map
  })

  return {
    annotations,
    selectedId,
    tool,
    color,
    strokeWidth,
    fontSize,
    author,
    pageSizes: computed(() => countByPage.value),
    countByPage,
    add,
    remove,
    update,
    clear,
    reset,
    undo,
    redo,
    select,
    byPage,
    exportJSON,
    importJSON,
    exportXFDF,
    importXFDF,
    canUndo: computed(() => undoStack.value.length > 0),
    canRedo: computed(() => redoStack.value.length > 0),
  }
}

export function normalizeAnnotationRect(rect: Rect): Rect {
  return normalizeRect(rect)
}

/** Rotate a normalized display point clockwise by `quarterTurns` × 90°. */
export function transformPointGeometry(point: Point, quarterTurns: number): Point {
  let result = point
  for (let turn = 0; turn < normalizeTurns(quarterTurns); turn += 1) {
    result = { x: 1 - result.y, y: result.x }
  }
  return result
}

/** Rotate a normalized display rectangle clockwise by `quarterTurns` × 90°. */
export function transformRectGeometry(rect: Rect, quarterTurns: number): Rect {
  let result = rect
  for (let turn = 0; turn < normalizeTurns(quarterTurns); turn += 1) {
    result = { x: 1 - result.y - result.h, y: result.x, w: result.h, h: result.w }
  }
  return result
}

/**
 * Return a display-oriented copy of an annotation. Annotations are persisted in
 * the page's unrotated display space, so zooming, reloading or exporting never
 * changes their geometry; only the overlay is transformed while rotating.
 */
export function transformAnnotationGeometry<T extends Annotation>(
  annotation: T,
  quarterTurns: number
): T {
  const turns = normalizeTurns(quarterTurns)
  if (!turns) return annotation
  const copy = { ...annotation } as Annotation
  if ("rects" in copy) {
    copy.rects = copy.rects.map((rect) => transformRectGeometry(rect, turns))
  } else if (copy.type === 'ink') {
    copy.paths = copy.paths.map((path) =>
      path.map((point) => transformPointGeometry(point, turns))
    )
  } else {
    copy.rect = transformRectGeometry(copy.rect, turns)
  }
  return copy as T
}

function normalizeTurns(quarterTurns: number): number {
  return ((Math.round(quarterTurns) % 4) + 4) % 4
}

export function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}
