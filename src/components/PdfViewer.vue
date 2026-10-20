<script setup lang="ts">
	import { computed, ref } from "vue";
	import VuePdfEmbed from "vue-pdf-embed";
	import "vue-pdf-embed/dist/styles/textLayer.css";
	import "vue-pdf-embed/dist/styles/annotationLayer.css";

	const props = withDefaults(
		defineProps<{
			source: string;
			width?: number;
			showToolbar?: boolean;
		}>(),
		{
			width: 980,
			showToolbar: true,
		}
	);

	const pageCount = ref(0);
	const currentPage = ref(1);
	const scale = ref(1);

	const scaledWidth = computed(() => Math.round(props.width * scale.value));

	function handleLoaded(pdf: { numPages: number }) {
		pageCount.value = pdf.numPages;
		currentPage.value = 1;
		scale.value = 1;
	}

	function prevPage() {
		if (currentPage.value > 1) currentPage.value--;
	}
	function nextPage() {
		if (currentPage.value < pageCount.value) currentPage.value++;
	}
	function zoomIn() {
		scale.value = Math.min(3, +(scale.value + 0.25).toFixed(2));
	}
	function zoomOut() {
		scale.value = Math.max(0.5, +(scale.value - 0.25).toFixed(2));
	}
	function resetZoom() {
		scale.value = 1;
	}
</script>

<template>
	<div class="pdf-viewer">
		<div v-if="showToolbar" class="pdf-toolbar">
			<button aria-label="Previous page" :disabled="currentPage <= 1" @click="prevPage">
				‹
			</button>
			<span class="pdf-toolbar-label">{{ currentPage }} / {{ pageCount || "–" }}</span>
			<button aria-label="Next page" :disabled="currentPage >= pageCount" @click="nextPage">
				›
			</button>
			<span class="pdf-toolbar-spacer" />
			<button aria-label="Zoom out" :disabled="scale <= 0.5" @click="zoomOut">−</button>
			<span class="pdf-toolbar-label">{{ Math.round(scale * 100) }}%</span>
			<button aria-label="Zoom in" :disabled="scale >= 3" @click="zoomIn">+</button>
			<button @click="resetZoom">reset</button>
		</div>
		<div class="pdf-canvas">
			<VuePdfEmbed
				v-if="source"
				:source="source"
				:page="currentPage"
				:width="scaledWidth"
				@loaded="handleLoaded" />
		</div>
	</div>
</template>

<style scoped>
	.pdf-viewer {
		display: flex;
		flex-direction: column;
	}

	.pdf-toolbar {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 6px 10px;
		border: 1px solid #ddd;
		border-bottom: none;
		border-radius: 6px 6px 0 0;
		background: #fafafa;
	}

	.pdf-toolbar button {
		min-width: 28px;
		padding: 4px 8px;
		border: 1px solid #ccc;
		border-radius: 4px;
		background: #fff;
		cursor: pointer;
		font-size: 14px;
	}

	.pdf-toolbar button:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}

	.pdf-toolbar-label {
		font-size: 13px;
		color: #333;
		min-width: 56px;
		text-align: center;
	}

	.pdf-toolbar-spacer {
		flex: 1;
	}

	.pdf-canvas {
		display: flex;
		justify-content: center;
		border: 1px solid #ddd;
		border-radius: 0 0 6px 6px;
		overflow: hidden;
		background: #525659;
	}
</style>
