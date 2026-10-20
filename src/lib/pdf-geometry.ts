import type { PageViewport } from 'pdfjs-dist'

/**
 * pdf.js 6 removed `convertToViewportRectangle`; converting two opposite
 * corners gives the same axis-aligned box and works under rotation.
 */
export function viewportRect(
  viewport: PageViewport,
  rect: [number, number, number, number]
): [number, number, number, number] {
  const [x1, y1, x2, y2] = rect
  const p1 = viewport.convertToViewportPoint(x1, y1) as [number, number]
  const p2 = viewport.convertToViewportPoint(x2, y2) as [number, number]
  return [
    Math.min(p1[0], p2[0]),
    Math.min(p1[1], p2[1]),
    Math.max(p1[0], p2[0]),
    Math.max(p1[1], p2[1]),
  ]
}
