import fs from 'node:fs/promises'
import express from 'express'

// Constants
const isProduction = process.env.NODE_ENV === 'production'
const port = process.env.PORT || 5173
const base = process.env.BASE || '/'
const converterUrl = (process.env.CONVERTER_URL || 'http://localhost:5080').replace(/\/$/, '')
const converterKey = process.env.CONVERTER_API_KEY || ''
const converterTimeoutMs = Number(process.env.CONVERTER_TIMEOUT_MS || 180000)

// Cached production assets
const templateHtml = isProduction
  ? await fs.readFile('./dist/client/index.html', 'utf-8')
  : ''
const ssrManifest = isProduction
  ? await fs.readFile('./dist/client/.vite/ssr-manifest.json', 'utf-8')
  : undefined

// Create http server
const app = express()

// Proxy PDF -> Office conversions to the self-hosted .NET service.
app.use('/api/convert', express.raw({ type: ['application/pdf', 'application/octet-stream'], limit: '50mb' }))
app.post('/api/convert/:format', async (req, res) => {
  const format = String(req.params.format || '').toLowerCase()
  if (format !== 'docx' && format !== 'xlsx') {
    res.status(404).json({ error: 'Supported formats are docx and xlsx.' })
    return
  }

  if (!Buffer.isBuffer(req.body) || req.body.length === 0) {
    res.status(400).json({ error: 'Send a PDF file in the request body.' })
    return
  }

  const name = typeof req.query.name === 'string' ? req.query.name : 'document.pdf'
  const target = new URL(`${converterUrl}/convert/${format}`)
  target.searchParams.set('name', name)

  const headers = { 'Content-Type': 'application/pdf' }
  if (converterKey) headers['X-Api-Key'] = converterKey

  try {
    const upstream = await fetch(target, {
      method: 'POST',
      headers,
      body: req.body,
      signal: AbortSignal.timeout(converterTimeoutMs)
    })

    const payload = Buffer.from(await upstream.arrayBuffer())
    const contentType = upstream.headers.get('content-type')
    const disposition = upstream.headers.get('content-disposition')
    if (contentType) res.setHeader('Content-Type', contentType)
    if (disposition) res.setHeader('Content-Disposition', disposition)
    res.status(upstream.status).send(payload)
  } catch (error) {
    console.error('[converter]', error)
    const message = error?.name === 'TimeoutError'
      ? 'The PDF conversion service timed out.'
      : 'The PDF conversion service is not reachable. Start the .NET converter service or set CONVERTER_URL.'
    res.status(502).json({ error: message })
  }
})

app.use((error, req, res, next) => {
  if (error?.type === 'entity.too.large') {
    res.status(413).json({ error: 'The PDF exceeds the 50 MB upload limit.' })
    return
  }
  next(error)
})

// Add Vite or respective production middlewares
let vite
if (!isProduction) {
  const { createServer } = await import('vite')
  vite = await createServer({
    server: { middlewareMode: true },
    appType: 'custom',
    base
  })
  app.use(vite.middlewares)
} else {
  const compression = (await import('compression')).default
  const sirv = (await import('sirv')).default
  app.use(compression())
  app.use(base, sirv('./dist/client', { extensions: [] }))
}

// Serve HTML
app.use('*', async (req, res) => {
  try {
    const url = req.originalUrl.replace(base, '')

    let template
    let render
    if (!isProduction) {
      // Always read fresh template in development
      template = await fs.readFile('./index.html', 'utf-8')
      template = await vite.transformIndexHtml(url, template)
      render = (await vite.ssrLoadModule('/src/entry-server.ts')).render
    } else {
      template = templateHtml
      render = (await import('./dist/server/entry-server.js')).render
    }

    const rendered = await render(url, ssrManifest)

    const html = template
      .replace(`<!--app-head-->`, rendered.head ?? '')
      .replace(`<!--app-html-->`, rendered.html ?? '')

    res.status(200).set({ 'Content-Type': 'text/html' }).send(html)
  } catch (e) {
    vite?.ssrFixStacktrace(e)
    console.log(e.stack)
    res.status(500).end(e.stack)
  }
})

// Start http server
app.listen(port, () => {
  console.log(`Server started at http://localhost:${port}`)
})
