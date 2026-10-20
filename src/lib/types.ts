/**
 * Public types for the viewer. Deliberately framework-agnostic so the same
 * shapes can be reused by plugins, controllers and exports.
 */

export type ToolbarItem =
  | 'open'
  | 'download'
  | 'print'
  | 'search'
  | 'pageNav'
  | 'zoom'
  | 'rotate'
  | 'thumbnails'
  | 'outline'
  | 'annotations'
  | 'fullscreen'
  | 'theme'
  | 'properties'

export interface ToolbarOptions {
  /** false hides the toolbar entirely (parity with `toolbarOptions: false`). */
  show?: boolean
  /** Which items to render, in order. Defaults to every item. */
  items?: ToolbarItem[]
  /** Hide individual items without rewriting the order. */
  download?: boolean
  print?: boolean
  open?: boolean
  search?: boolean
  fullscreen?: boolean
  theme?: boolean
  rotate?: boolean
  properties?: boolean
  thumbnails?: boolean
  outline?: boolean
  annotations?: boolean
  pageNav?: boolean
  zoom?: boolean
}

export type Theme = 'light' | 'dark' | 'auto'

export type FitMode = 'none' | 'width' | 'page'

export type AnnotationTool =
  | 'cursor'
  | 'highlight'
  | 'underline'
  | 'strikeout'
  | 'ink'
  | 'square'
  | 'circle'
  | 'note'
  | 'image'

export interface Point {
  /** 0..1 relative to page width */
  x: number
  /** 0..1 relative to page height */
  y: number
}

export interface Rect {
  x: number
  y: number
  w: number
  h: number
}

export interface AnnotationBase {
  id: string
  page: number
  type: string
  color: string
  author: string
  createdAt: string
  updatedAt?: string
  note?: string
}

export interface TextMarkupAnnotation extends AnnotationBase {
  type: 'highlight' | 'underline' | 'strikeout'
  rects: Rect[]
  text?: string
}

export interface InkAnnotation extends AnnotationBase {
  type: 'ink'
  paths: Point[][]
  width: number
}

export interface ShapeAnnotation extends AnnotationBase {
  type: 'square' | 'circle'
  rect: Rect
  width: number
}

export interface FreeTextAnnotation extends AnnotationBase {
  type: 'note'
  rect: Rect
  text: string
  fontSize: number
}

export interface ImageAnnotation extends AnnotationBase {
  type: 'image'
  rect: Rect
  src: string
}

export type Annotation =
  | TextMarkupAnnotation
  | InkAnnotation
  | ShapeAnnotation
  | FreeTextAnnotation
  | ImageAnnotation

/** Distributes Omit across the annotation union instead of collapsing it. */
export type DistributiveOmit<T, K extends PropertyKey> = T extends unknown ? Omit<T, K> : never

export type NewAnnotation = DistributiveOmit<Annotation, "id" | "createdAt" | "author"> & {
  id?: string
  author?: string
}

/** Search result computed from the PDF text layer. */
export interface SearchResult {
  page: number
  index: number
  rect: Rect
  snippet: string
}

export interface DocumentProperties {
  title?: string
  author?: string
  subject?: string
  keywords?: string
  creator?: string
  producer?: string
  creationDate?: string
  modificationDate?: string
  pages: number
  fileSize?: number
}

export interface ViewerController {
  goToPage(page: number): void
  nextPage(): void
  previousPage(): void
  getPage(): number
  getPageCount(): number
}

export interface ZoomController {
  zoomIn(step?: number): void
  zoomOut(step?: number): void
  setZoom(scale: number): void
  getZoom(): number
  fitWidth(): void
  fitPage(): void
}

export interface RotateController {
  rotateClockwise(): void
  rotateCounterClockwise(): void
  setRotation(degrees: number): void
  getRotation(): number
}

export interface SearchController {
  search(query: string): Promise<SearchResult[]>
  next(): void
  previous(): void
  clear(): void
  getResults(): SearchResult[]
}

export interface AnnotationController {
  add(annotation: NewAnnotation): Annotation
  remove(id: string): void
  update(id: string, patch: Partial<Annotation>): void
  getAll(): Annotation[]
  clear(): void
  undo(): void
  redo(): void
  canUndo(): boolean
  canRedo(): boolean
  select(id: string | null): void
  getSelected(): string | null
  exportJSON(): string
  importJSON(json: string): number
  exportXFDF(): string
  importXFDF(xml: string): number
  setTool(tool: AnnotationTool): void
  getTool(): AnnotationTool
  setColor(color: string): void
  getColor(): string
  save(): Promise<void>
  load(): Promise<void>
}

export interface ViewerExposed {
  pageController: ViewerController
  zoomController: ZoomController
  rotateController: RotateController
  searchController: SearchController
  downloadController: { download(): Promise<void> }
  printController: { print(): Promise<void> }
  annotationController?: AnnotationController
  doc: unknown
}
