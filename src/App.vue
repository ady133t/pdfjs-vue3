<script setup lang="ts">
	import { defineAsyncComponent, h } from "vue";
	import ClientOnly from "./components/ClientOnly.vue";
	import VPdfAnnotationPlugin from "./plugins/annotation";
	import type { ToolbarOptions } from "./lib/types";

	const SAMPLE_PDF =
		"https://raw.githubusercontent.com/mozilla/pdf.js/ba2edeae/web/compressed.tracemonkey-pldi-09.pdf";

	const WithoutToolbar: ToolbarOptions = { show: false };

	const plugins = [VPdfAnnotationPlugin({ author: "Kanha", storageKey: "demo" })];

	const VPdfViewerComp = defineAsyncComponent(async () => {
		if (import.meta.env.SSR) return Promise.resolve(h("div", "Loading..."));
		const module = await import("./components/PdfViewer.vue");
		return module.default;
	});

	const features = [
		"Canvas + text layer",
		"Continuous scroll",
		"Search",
		"Thumbnails",
		"Outline",
		"Annotations",
		"XFDF import/export",
		"Print & download",
		"Word/Excel export",
		"Password protected PDFs",
		"Dark mode",
		"Keyboard shortcuts",
		"SSR safe",
	];
</script>

<template>
	<main class="page">
		<header class="hero">
			<p class="eyebrow">MIT licensed · no keys · no watermarks</p>
			<h1>Open Source PDF Viewer</h1>
			<p class="lede">
				A complete Vue 3 + TypeScript PDF viewer for SSR apps, built directly on
				<code>pdfjs-dist</code>. Everything below is open source and self-hosted.
			</p>
			<ul class="features">
				<li v-for="feature in features" :key="feature">{{ feature }}</li>
			</ul>
		</header>

		<section class="demo">
			<div class="demo-head">
				<h2>Default toolbar</h2>
				<p>
					Page navigation, zoom, fit, rotate, search, thumbnails, outline,
					download, print, and Word/Excel conversion.
				</p>
			</div>
			<div class="viewer-frame">
				<ClientOnly>
					<VPdfViewerComp
						:src="SAMPLE_PDF"
						:height="780"
						filename="trace-based-jit.pdf"
						office-export />
				</ClientOnly>
			</div>
		</section>

		<section class="demo">
			<div class="demo-head">
				<h2>Without toolbar</h2>
				<p>Bare document surface via <code>toolbarOptions: false</code>.</p>
			</div>
			<div class="viewer-frame">
				<ClientOnly>
					<VPdfViewerComp :src="SAMPLE_PDF" :height="640" :toolbar-options="WithoutToolbar" />
				</ClientOnly>
			</div>
		</section>

		<section class="demo demo-mobile">
			<div class="demo-head">
				<h2>Mobile</h2>
				<p>Responsive toolbar and overlay sidebar at 468px.</p>
			</div>
			<div class="viewer-frame mobile">
				<ClientOnly>
					<VPdfViewerComp :src="SAMPLE_PDF" :height="620" />
				</ClientOnly>
			</div>
		</section>

		<section class="demo">
			<div class="demo-head">
				<h2>Annotations</h2>
				<p>
					Highlight, underline, strike out, ink, shapes, notes and images. Export to JSON or
					XFDF, import back, undo/redo.
				</p>
			</div>
			<div class="viewer-frame">
				<ClientOnly>
					<VPdfViewerComp :src="SAMPLE_PDF" :height="780" :plugins="plugins" />
				</ClientOnly>
			</div>
		</section>

		<section class="demo">
			<div class="demo-head">
				<h2>Single page mode</h2>
				<p>One page at a time with <code>scroll-mode="single"</code>.</p>
			</div>
			<div class="viewer-frame">
				<ClientOnly>
					<VPdfViewerComp :src="SAMPLE_PDF" :height="640" scroll-mode="single" />
				</ClientOnly>
			</div>
		</section>
	</main>
</template>

<style scoped>
	.page {
		width: min(1180px, 100%);
		margin: 0 auto;
		padding: 40px 24px 80px;
		box-sizing: border-box;
		text-align: left;
	}

	.hero {
		max-width: 780px;
		margin: 0 auto 48px;
		text-align: center;
	}

	.eyebrow {
		margin: 0 0 10px;
		text-transform: uppercase;
		letter-spacing: 0.14em;
		font-size: 11px;
		color: #7c8aa5;
	}

	.hero h1 {
		margin: 0 0 14px;
		font-size: clamp(2rem, 4.4vw, 3.4rem);
		line-height: 1.05;
		letter-spacing: -0.02em;
	}

	.lede {
		margin: 0 auto;
		max-width: 640px;
		color: #9aa6bb;
		line-height: 1.6;
	}

	code {
		padding: 1px 5px;
		border-radius: 4px;
		background: rgba(124, 147, 255, 0.14);
		color: #a8b8ff;
		font-size: 0.92em;
	}

	.features {
		display: flex;
		flex-wrap: wrap;
		justify-content: center;
		gap: 6px;
		margin: 20px auto 0;
		padding: 0;
		list-style: none;
	}

	.features li {
		padding: 4px 10px;
		border: 1px solid #333a4a;
		border-radius: 999px;
		font-size: 11px;
		color: #9aa6bb;
	}

	.demo {
		margin-bottom: 48px;
	}

	.demo-head {
		margin-bottom: 12px;
	}

	.demo-head h2 {
		margin: 0 0 4px;
		font-size: 1.15rem;
		letter-spacing: -0.01em;
	}

	.demo-head p {
		margin: 0;
		color: #8d99ae;
		font-size: 13px;
	}

	.viewer-frame {
		height: auto;
		border-radius: 12px;
		overflow: hidden;
		box-shadow: 0 18px 50px rgba(0, 0, 0, 0.4);
	}

	.demo-mobile .viewer-frame {
		width: min(468px, 100%);
		margin: 0 auto;
	}

	@media (max-width: 720px) {
		.page {
			padding: 28px 12px 60px;
		}
	}
</style>
