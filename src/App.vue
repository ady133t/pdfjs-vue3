<script setup lang="ts">
	import { defineAsyncComponent, h } from "vue";
	import ClientOnly from "./components/ClientOnly.vue";

	const SAMPLE_PDF =
		"https://raw.githubusercontent.com/mozilla/pdf.js/ba2edeae/web/compressed.tracemonkey-pldi-09.pdf";

	const PdfViewer = defineAsyncComponent(async () => {
		if (import.meta.env.SSR) return Promise.resolve(h("div", "Loading..."));
		const module = await import("./components/PdfViewer.vue");
		return module.default;
	});
</script>

<template>
	<div>
		<h1>Open Source PDF Viewer: Vue 3 + SSR + TypeScript</h1>
		<p>
			Powered by <code>vue-pdf-embed</code> (MIT) on top of <code>pdfjs-dist</code>. No
			licenses, no watermarks.
		</p>

		<h2>Default Toolbar</h2>
		<ClientOnly class="pdf-viewer-wrapper">
			<PdfViewer :source="SAMPLE_PDF" />
		</ClientOnly>

		<h2>Without Toolbar</h2>
		<ClientOnly class="pdf-viewer-wrapper no-toolbar">
			<PdfViewer :source="SAMPLE_PDF" :show-toolbar="false" />
		</ClientOnly>

		<h2>Mobile</h2>
		<ClientOnly class="pdf-viewer-wrapper-mobile">
			<PdfViewer :source="SAMPLE_PDF" :width="468" />
		</ClientOnly>
	</div>
</template>

<style scoped>
	.pdf-viewer-wrapper {
		width: 1028px;
		margin: 0 auto;
	}

	.pdf-viewer-wrapper-mobile {
		width: 468px;
		margin: 0 auto;
	}
</style>
