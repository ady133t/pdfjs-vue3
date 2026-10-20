<script setup lang="ts">
	import { computed, onBeforeUnmount, ref, shallowRef, watch } from "vue";
	import type { PDFDocumentProxy } from "pdfjs-dist";
	import type { Annotation } from "../lib/types";
	import PdfOutline from "./PdfOutline.vue";

	const props = defineProps<{
		mode: string;
		doc: PDFDocumentProxy;
		page: number;
		annotations: Annotation[];
		selectedId?: string | null;
		hasAnnotation?: boolean;
	}>();

	const rootEl = ref<HTMLElement | null>(null);

	const emit = defineEmits<{
		(e: "close"): void;
		(e: "switch", mode: string): void;
		(e: "go-to", page: number): void;
		(e: "navigate-dest", dest: unknown): void;
		(e: "select-annotation", id: string): void;
		(e: "delete-annotation", id: string): void;
		(e: "clear-annotations"): void;
		(e: "export-annotations", format: "json" | "xfdf"): void;
		(e: "import-annotations", payload: { format: "json" | "xfdf"; text: string }): void;
	}>();

	interface OutlineNode {
		title: string;
		dest: unknown;
		items?: OutlineNode[];
		depth?: number;
	}

	const outline = shallowRef<OutlineNode[]>([]);
	const outlineLoading = ref(false);
	const outlineError = ref("");
	const activeTab = computed(() => props.mode);

	const thumbWidth = 132;
	const thumbs = ref<Record<number, string>>({});
	const renderingPages = new Set<number>();
	const thumbToken = ref(0);
	let observer: IntersectionObserver | null = null;

	const pageAnnotations = computed(() =>
		[...props.annotations].sort((a, b) => (a.page === b.page ? a.createdAt.localeCompare(b.createdAt) : a.page - b.page))
	);

	const typeLabels: Record<string, string> = {
		highlight: "Highlight",
		underline: "Underline",
		strikeout: "Strikethrough",
		ink: "Ink",
		square: "Rectangle",
		circle: "Circle",
		note: "Note",
		image: "Image",
	};

	async function loadOutline() {
		if (!props.doc || props.mode !== "outline") return;
		if (outline.value.length || outlineLoading.value) return;
		outlineLoading.value = true;
		outlineError.value = "";
		try {
			const raw = (await props.doc.getOutline()) as OutlineNode[] | null;
			outline.value = raw ?? [];
		} catch (error) {
			outlineError.value = "Could not read the table of contents.";
			console.error("[viewer] outline failed", error);
		} finally {
			outlineLoading.value = false;
		}
	}

	async function renderThumb(pageNumber: number) {
		if (thumbs.value[pageNumber] || renderingPages.has(pageNumber)) return;
		const token = thumbToken.value;
		renderingPages.add(pageNumber);
		try {
			const page = await props.doc.getPage(pageNumber);
			const base = page.getViewport({ scale: 1 });
			const scale = thumbWidth / base.width;
			const viewport = page.getViewport({ scale });
			const canvas = document.createElement("canvas");
			const ratio = Math.min(window.devicePixelRatio || 1, 2);
			canvas.width = Math.floor(viewport.width * ratio);
			canvas.height = Math.floor(viewport.height * ratio);
			const context = canvas.getContext("2d");
			if (!context) return;
			await page.render({
				canvas,
				canvasContext: context,
				viewport,
				transform: ratio !== 1 ? [ratio, 0, 0, ratio, 0, 0] : undefined,
			}).promise;
			if (token !== thumbToken.value) return;
			thumbs.value = { ...thumbs.value, [pageNumber]: canvas.toDataURL("image/jpeg", 0.72) };
		} catch (error) {
			console.warn(`[viewer] thumbnail ${pageNumber} failed`, error);
		} finally {
			renderingPages.delete(pageNumber);
		}
	}

	function observeThumbs() {
		observer?.disconnect();
		if (typeof IntersectionObserver === "undefined") return;
		observer = new IntersectionObserver(
			(entries) => {
				for (const entry of entries) {
					if (!entry.isIntersecting) continue;
					const page = Number((entry.target as HTMLElement).dataset.page);
					if (!page) continue;
					observer?.unobserve(entry.target);
					renderThumb(page);
				}
			},
			{ root: null, rootMargin: "200px" }
		);
		for (const element of Array.from(
			rootEl.value?.querySelectorAll<HTMLElement>(".pdf-thumb[data-page]") ?? []
		)) {
			observer.observe(element);
		}
	}

	watch(
		() => props.doc,
		() => {
			thumbs.value = {};
			thumbToken.value += 1;
			outline.value = [];
		}
	);

	watch(
		() => props.mode,
		async (mode) => {
			if (mode === "outline") await loadOutline();
			if (mode === "thumbnails") {
				setTimeout(observeThumbs, 30);
			}
		},
		{ immediate: true }
	);

	function onImport(event: Event) {
		const input = event.target as HTMLInputElement;
		const file = input.files?.[0];
		if (!file) return;
		const format = file.name.toLowerCase().endsWith(".xfdf") ? "xfdf" : "json";
		const reader = new FileReader();
		reader.onload = () => {
			emit("import-annotations", { format, text: String(reader.result ?? "") });
			input.value = "";
		};
		reader.readAsText(file);
	}

	onBeforeUnmount(() => observer?.disconnect());
</script>

<template>
	<aside ref="rootEl" class="pdf-sidebar" aria-label="PDF navigation">
		<div class="pdf-sidebar-tabs">
			<button
				class="pdf-sidebar-tab"
				:class="{ 'is-active': activeTab === 'thumbnails' }"
				title="Thumbnails"
				@click="emit('switch', 'thumbnails')">
				Pages
			</button>
			<button
				class="pdf-sidebar-tab"
				:class="{ 'is-active': activeTab === 'outline' }"
				title="Table of contents"
				@click="emit('switch', 'outline')">
				Contents
			</button>
			<button
				v-if="hasAnnotation"
				class="pdf-sidebar-tab"
				:class="{ 'is-active': activeTab === 'annotations' }"
				title="Annotations"
				@click="emit('switch', 'annotations')">
				Notes
			</button>
			<button class="pdf-sidebar-close" title="Close panel" aria-label="Close panel" @click="emit('close')">
				×
			</button>
		</div>

		<div v-if="activeTab === 'thumbnails'" class="pdf-thumb-list">
			<button
				v-for="pageNumber in doc.numPages"
				:key="pageNumber"
				class="pdf-thumb"
				:class="{ 'is-active': pageNumber === page }"
				:data-page="pageNumber"
				:aria-label="`Go to page ${pageNumber}`"
				@click="emit('go-to', pageNumber)">
				<img v-if="thumbs[pageNumber]" :src="thumbs[pageNumber]" :alt="`Page ${pageNumber}`" />
				<span v-else class="pdf-thumb-placeholder" />
				<span class="pdf-thumb-number">{{ pageNumber }}</span>
			</button>
		</div>

		<div v-else-if="activeTab === 'outline'" class="pdf-outline">
			<p v-if="outlineLoading" class="pdf-sidebar-empty">Reading contents…</p>
			<p v-else-if="outlineError" class="pdf-sidebar-empty">{{ outlineError }}</p>
			<p v-else-if="!outline.length" class="pdf-sidebar-empty">
				This document has no table of contents.
			</p>
			<PdfOutline v-else :items="outline" @navigate="emit('navigate-dest', $event)" />
		</div>

		<div v-else-if="activeTab === 'annotations'" class="pdf-annotation-panel">
			<div v-if="pageAnnotations.length" class="pdf-annotation-list">
				<button
					v-for="annotation in pageAnnotations"
					:key="annotation.id"
					class="pdf-annotation-row"
					:class="{ 'is-active': annotation.id === selectedId }"
					@click="emit('select-annotation', annotation.id)">
					<span class="pdf-annotation-swatch" :style="{ backgroundColor: annotation.color }" />
					<span class="pdf-annotation-body">
						<span class="pdf-annotation-meta">
							Page {{ annotation.page }} · {{ typeLabels[annotation.type] || annotation.type }}
						</span>
						<span class="pdf-annotation-snippet">
							{{ annotation.note || ("text" in annotation ? annotation.text : "") || "No text" }}
						</span>
					</span>
					<span
						class="pdf-annotation-remove"
						title="Delete annotation"
						role="button"
						tabindex="0"
						@click.stop="emit('delete-annotation', annotation.id)"
						@keydown.enter.stop="emit('delete-annotation', annotation.id)">
						×
					</span>
				</button>
			</div>
			<p v-else class="pdf-sidebar-empty">
				No annotations yet. Pick a tool above and drag over the page.
			</p>

			<div class="pdf-annotation-actions">
				<button class="pdf-text-button" @click="emit('export-annotations', 'json')">
					Export JSON
				</button>
				<button class="pdf-text-button" @click="emit('export-annotations', 'xfdf')">
					Export XFDF
				</button>
				<label class="pdf-text-button">
					Import
					<input
						type="file"
						accept=".json,.xfdf,application/json,application/xml,text/xml"
						hidden
						@change="onImport" />
				</label>
				<button
					class="pdf-text-button is-danger"
					:disabled="!pageAnnotations.length"
					@click="emit('clear-annotations')">
					Clear all
				</button>
			</div>
		</div>
	</aside>
</template>

<style scoped>
	.pdf-sidebar {
		width: 244px;
		flex: none;
		display: flex;
		flex-direction: column;
		border-right: 1px solid var(--pdf-border, #dcdfe6);
		background: var(--pdf-sidebar-bg, #fbfbfd);
		color: var(--pdf-fg, #2f3542);
		min-height: 0;
	}

	.pdf-sidebar-tabs {
		display: flex;
		align-items: stretch;
		gap: 2px;
		padding: 6px 6px 0;
		border-bottom: 1px solid var(--pdf-border, #dcdfe6);
	}

	.pdf-sidebar-tab {
		flex: 1;
		padding: 8px 4px;
		border: none;
		border-bottom: 2px solid transparent;
		background: transparent;
		color: var(--pdf-muted, #6b7280);
		font: inherit;
		font-size: 12px;
		cursor: pointer;
	}

	.pdf-sidebar-tab.is-active {
		color: var(--pdf-accent, #4c6ef5);
		border-bottom-color: var(--pdf-accent, #4c6ef5);
	}

	.pdf-sidebar-close {
		width: 28px;
		border: none;
		background: transparent;
		color: var(--pdf-muted, #6b7280);
		font-size: 20px;
		line-height: 1;
		cursor: pointer;
	}

	.pdf-thumb-list {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 8px;
		padding: 10px;
		overflow: auto;
		align-content: start;
	}

	.pdf-thumb {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 4px;
		padding: 4px;
		border: 1px solid transparent;
		border-radius: 6px;
		background: transparent;
		cursor: pointer;
	}

	.pdf-thumb img,
	.pdf-thumb-placeholder {
		width: 100%;
		height: auto;
		display: block;
		border: 1px solid #d3d7de;
		border-radius: 3px;
		background: #fff;
		box-shadow: 0 1px 2px rgba(0, 0, 0, 0.12);
	}

	.pdf-thumb-placeholder {
		aspect-ratio: 0.72;
		background: linear-gradient(100deg, #f1f3f6 30%, #fafbfc 50%, #f1f3f6 70%);
		background-size: 200% 100%;
		animation: pdf-thumb-shimmer 1.4s linear infinite;
	}

	@keyframes pdf-thumb-shimmer {
		from {
			background-position: 150% 0;
		}
		to {
			background-position: -50% 0;
		}
	}

	.pdf-thumb.is-active {
		border-color: var(--pdf-accent, #4c6ef5);
		background: var(--pdf-accent-soft, #e7edff);
	}

	.pdf-thumb-number {
		font-size: 11px;
		color: var(--pdf-muted, #6b7280);
	}

	.pdf-outline {
		padding: 12px 10px;
		overflow: auto;
	}

	.pdf-sidebar-empty {
		margin: 14px 10px;
		color: var(--pdf-muted, #6b7280);
		font-size: 12px;
		line-height: 1.5;
	}

	.pdf-annotation-panel {
		display: flex;
		flex-direction: column;
		min-height: 0;
		flex: 1;
	}

	.pdf-annotation-list {
		overflow: auto;
		padding: 8px;
		display: flex;
		flex-direction: column;
		gap: 4px;
	}

	.pdf-annotation-row {
		display: flex;
		align-items: flex-start;
		gap: 8px;
		width: 100%;
		padding: 7px 8px;
		border: 1px solid transparent;
		border-radius: 7px;
		background: transparent;
		color: inherit;
		font: inherit;
		text-align: left;
		cursor: pointer;
	}

	.pdf-annotation-row:hover {
		background: var(--pdf-hover, #eef1f7);
	}

	.pdf-annotation-row.is-active {
		border-color: var(--pdf-accent, #4c6ef5);
		background: var(--pdf-accent-soft, #e7edff);
	}

	.pdf-annotation-swatch {
		width: 10px;
		height: 10px;
		margin-top: 3px;
		border-radius: 50%;
		flex: none;
		box-shadow: inset 0 0 0 1px rgba(0, 0, 0, 0.2);
	}

	.pdf-annotation-body {
		display: flex;
		flex-direction: column;
		gap: 2px;
		min-width: 0;
		flex: 1;
	}

	.pdf-annotation-meta {
		font-size: 10px;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--pdf-muted, #6b7280);
	}

	.pdf-annotation-snippet {
		font-size: 12px;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.pdf-annotation-remove {
		border: none;
		background: transparent;
		color: var(--pdf-muted, #6b7280);
		font-size: 16px;
		line-height: 1;
		cursor: pointer;
		padding: 0 2px;
	}

	.pdf-annotation-remove:hover {
		color: #c92a2a;
	}

	.pdf-annotation-actions {
		display: flex;
		flex-wrap: wrap;
		gap: 6px;
		padding: 10px;
		border-top: 1px solid var(--pdf-border, #dcdfe6);
	}

	.pdf-text-button {
		padding: 5px 8px;
		border: 1px solid var(--pdf-border, #dcdfe6);
		border-radius: 6px;
		background: var(--pdf-input-bg, #fff);
		color: inherit;
		font: inherit;
		font-size: 11px;
		cursor: pointer;
	}

	.pdf-text-button:hover:not(:disabled) {
		border-color: var(--pdf-accent, #4c6ef5);
		color: var(--pdf-accent, #4c6ef5);
	}

	.pdf-text-button:disabled {
		opacity: 0.45;
		cursor: not-allowed;
	}

	.pdf-text-button.is-danger:hover:not(:disabled) {
		border-color: #c92a2a;
		color: #c92a2a;
	}

	@container pdfviewer (max-width: 720px) {
		.pdf-sidebar {
			position: absolute;
			inset: 0 auto 0 0;
			z-index: 20;
			width: min(78vw, 280px);
			box-shadow: 0 0 24px rgba(0, 0, 0, 0.28);
		}
	}
</style>
