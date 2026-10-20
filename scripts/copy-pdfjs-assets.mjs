import { cp, mkdir, rm } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

// pdf.js needs its cmaps, standard fonts and wasm decoders at runtime.
// Copy them into public/ so every asset is served from our own origin.
const root = dirname(fileURLToPath(import.meta.url))
const source = join(root, '..', 'node_modules', 'pdfjs-dist')
const target = join(root, '..', 'public', 'pdfjs')

const folders = ['cmaps', 'standard_fonts', 'wasm']

await mkdir(target, { recursive: true })

for (const folder of folders) {
  const from = join(source, folder)
  if (!existsSync(from)) continue
  const to = join(target, folder)
  await rm(to, { recursive: true, force: true })
  await cp(from, to, { recursive: true })
}

console.log(`[pdfjs] assets synced to public/pdfjs`)
