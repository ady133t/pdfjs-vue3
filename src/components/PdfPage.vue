<script setup lang="ts">
	import {
		computed,
		nextTick,
		onBeforeUnmount,
		ref,
		shallowRef,
		watch,
	} from "vue";
	import { TextLayer } from "pdfjs-dist";
	import type { PDFDocumentProxy, PDFPageProxy, PageViewport, RenderTask } from "pdfjs-dist";
	import AnnotationView from "./AnnotationView.vue";
	import type { AnnotationStore } from "../lib/annotation-store";
	import type { Annotation, AnnotationTool, Point, Rect, SearchResult } from "../lib/types";
	import { normalizeAnnotationRect, transformAnnotationGeometry, transformPointGeometry, transformRectGeometry } from "../lib/annotations";
	import { viewportRect } from "../lib/pdf-geometry";

	const props = defineProps<{
		doc: PDFDocumentProxy;
		pageNumber: number;
		scale: number;
		extraRotation: number;
		dark?: boolean;
		tool?: AnnotationTool;
		color?: string;
		strokeWidth?: number;
		results?: SearchResult[];
		activeIndex?: number;
		selectedId?: string | null;
		store?: AnnotationStore;
		pendingImage?: string | null;
	}>();

	const emit = defineEmits<{
		(
			e: "page-ready",
			payload: { page: number; width: number; height: number; rotate: number }
		): void;
		(e: "navigate", dest: unknown): void;
		(e: "request-image"): void;
	}>();

	const rootEl = ref<HTMLDivElement | null>(null);
	const canvasEl = ref<HTMLCanvasElement | null>(null);
	const textEl = ref<HTMLDivElement | null>(null);
	const surfaceEl = ref<HTMLDivElement | null>(null);

	const pageProxy = shallowRef<PDFPageProxy | null>(null);
	const viewport = shallowRef<PageViewport | null>(null);
	const baseRotation = ref(0);
	const width = ref(0);
	const height = ref(0);
	const rendered = ref(false);
	const failed = ref(false);

	const editingId = ref<string | null>(null);
	const editingText = ref("");
	const editingStyle = ref<Record<string, string>>({});

	const draftPoints = ref<Point[]>([]);
	const draftStart = ref<Point | null>(null);
	const draftRect = ref<Rect | null>(null);

	let renderTask: RenderTask | null = null;
	let textLayer: TextLayer | null = null;
	let renderToken = 0;
	let drawing = false;

	const drawTools: AnnotationTool[] = ["ink", "square", "circle", "note", "image"];
	const markupTools: AnnotationTool[] = ["highlight", "underline", "strikeout"];

	const isDrawTool = computed(() => drawTools.includes((props.tool ?? "cursor") as AnnotationTool));
	const isMarkupTool = computed(() =>
		markupTools.includes((props.tool ?? "cursor") as AnnotationTool)
	);

	const pageAnnotations = computed<Annotation[]>(() => {
		if (!props.store) return [];
		const turns = Math.round(props.extraRotation / 90);
		return props.store.annotations
			.filter((annotation) => annotation.page === props.pageNumber)
			.map((annotation) => transformAnnotationGeometry(annotation, turns));
	});

	const interactiveAnnotations = computed(() => (props.tool ?? "cursor") === "cursor");

	const links = ref<
		Array<{ x: number; y: number; w: number; h: number; url?: string; dest?: unknown }>
	>([]);

	function rectStyle(rect: Rect): Record<string, string> {
		return {
			left: `${rect.x * width.value}px`,
			top: `${rect.y * height.value}px`,
			width: `${rect.w * width.value}px`,
			height: `${rect.h * height.value}px`,
		};
	}

	async function loadPage() {
		const doc = props.doc;
		if (!doc) return;
		try {
			const page = await doc.getPage(props.pageNumber);
			pageProxy.value = page;
			baseRotation.value = page.rotate;
			const view = page.view as number[];
			emit("page-ready", {
				page: props.pageNumber,
				width: view[2] - view[0],
				height: view[3] - view[1],
				rotate: page.rotate,
			});
			await render();
		} catch (error) {
			failed.value = true;
			console.error(`[viewer] failed to load page ${props.pageNumber}`, error);
		}
	}

	async function render() {
		const page = pageProxy.value;
		const canvas = canvasEl.value;
		if (!page || !canvas) return;

		const token = ++renderToken;
		renderTask?.cancel();

		const nextViewport = page.getViewport({
			scale: props.scale,
			rotation: baseRotation.value + props.extraRotation,
		});
		viewport.value = nextViewport;
		width.value = Math.floor(nextViewport.width);
		height.value = Math.floor(nextViewport.height);

		await nextTick();
		if (token !== renderToken) return;

		const context = canvas.getContext("2d", { alpha: false });
		if (!context) return;

		const ratio = Math.min(window.devicePixelRatio || 1, 2);
		canvas.width = Math.max(1, Math.floor(nextViewport.width * ratio));
		canvas.height = Math.max(1, Math.floor(nextViewport.height * ratio));
		canvas.style.width = `${nextViewport.width}px`;
		canvas.style.height = `${nextViewport.height}px`;

		const task = page.render({
			canvas,
			canvasContext: context,
			viewport: nextViewport,
			transform: ratio !== 1 ? [ratio, 0, 0, ratio, 0, 0] : undefined,
		});
		renderTask = task;

		try {
			await task.promise;
		} catch (error) {
			const name = (error as { name?: string })?.name;
			if (name === "RenderingCancelledException") return;
			failed.value = true;
			console.error(`[viewer] failed to render page ${props.pageNumber}`, error);
			return;
		}

		if (token !== renderToken) return;
		rendered.value = true;

		await renderTextLayer(page, nextViewport, token);
		await renderLinks(page, nextViewport, token);
	}

	async function renderTextLayer(page: PDFPageProxy, vp: PageViewport, token: number) {
		const container = textEl.value;
		if (!container) return;

		textLayer?.cancel();
		container.replaceChildren();
		container.style.setProperty("--total-scale-factor", String(vp.scale));
		container.style.setProperty("--scale-round-x", "1px");
		container.style.setProperty("--scale-round-y", "1px");
		container.style.width = `${vp.width}px`;
		container.style.height = `${vp.height}px`;

		try {
			const layer = new TextLayer({
				textContentSource: page.streamTextContent({ includeMarkedContent: true }),
				container,
				viewport: vp,
			});
			textLayer = layer;
			await layer.render();
			if (token !== renderToken) return;
		} catch (error) {
			const name = (error as { name?: string })?.name;
			if (name !== "AbortException") {
				console.warn("[viewer] text layer failed", error);
			}
		}
	}

	async function renderLinks(page: PDFPageProxy, vp: PageViewport, token: number) {
		try {
			const annotations = (await page.getAnnotations({ intent: "display" })) as Array<{
				subtype?: string;
				rect?: number[];
				url?: string;
				dest?: unknown;
			}>;
			if (token !== renderToken) return;

			const next: typeof links.value = [];
			for (const annotation of annotations) {
				if (annotation.subtype !== "Link" || !annotation.rect) continue;
				const [x1, y1, x2, y2] = viewportRect(vp, annotation.rect as [number, number, number, number]);
				const left = Math.min(x1, x2);
				const top = Math.min(y1, y2);
				next.push({
					x: left,
					y: top,
					w: Math.abs(x2 - x1),
					h: Math.abs(y2 - y1),
					url: annotation.url,
					dest: annotation.dest,
				});
			}
			links.value = next;
		} catch {
			links.value = [];
		}
	}

	function openLink(link: { url?: string; dest?: unknown }) {
		if (link.url) {
			window.open(link.url, "_blank", "noopener,noreferrer");
			return;
		}
		if (link.dest) emit("navigate", link.dest);
	}

	function normalizedPoint(event: PointerEvent): Point {
		const box = rootEl.value?.getBoundingClientRect();
		if (!box) return { x: 0, y: 0 };
		return {
			x: Math.min(1, Math.max(0, (event.clientX - box.left) / box.width)),
			y: Math.min(1, Math.max(0, (event.clientY - box.top) / box.height)),
		};
	}

	function toBasePoint(point: Point): Point {
		return transformPointGeometry(point, -props.extraRotation / 90);
	}

	function toBaseRect(rect: Rect): Rect {
		return normalizeAnnotationRect(transformRectGeometry(rect, -props.extraRotation / 90));
	}

	function onPointerDown(event: PointerEvent) {
		if (!isDrawTool.value || event.button !== 0) return;
		const tool = props.tool;

		if (tool === "note") {
			createNote(normalizedPoint(event));
			return;
		}

		if (tool === "image") {
			if (!props.pendingImage) {
				emit("request-image");
				return;
			}
			placeImage(normalizedPoint(event), props.pendingImage);
			return;
		}

		drawing = true;
		try {
			surfaceEl.value?.setPointerCapture(event.pointerId);
		} catch {
			/* synthetic or unsupported pointer */
		}
		const point = normalizedPoint(event);
		draftStart.value = point;
		draftPoints.value = [point];
		draftRect.value = { x: point.x, y: point.y, w: 0, h: 0 };
	}

	function onPointerMove(event: PointerEvent) {
		if (!drawing) return;
		const point = normalizedPoint(event);
		if (props.tool === "ink") {
			const last = draftPoints.value[draftPoints.value.length - 1];
			if (last && Math.hypot(point.x - last.x, point.y - last.y) < 0.002) return;
			draftPoints.value = [...draftPoints.value, point];
			return;
		}
		const start = draftStart.value;
		if (!start) return;
		draftRect.value = {
			x: Math.min(start.x, point.x),
			y: Math.min(start.y, point.y),
			w: Math.abs(point.x - start.x),
			h: Math.abs(point.y - start.y),
		};
	}

	function onPointerUp(event: PointerEvent) {
		if (!drawing) return;
		drawing = false;
		try {
			surfaceEl.value?.releasePointerCapture?.(event.pointerId);
		} catch {
			/* pointer was never captured */
		}
		const store = props.store;
		const tool = props.tool;
		if (!store || !tool) return;

		if (tool === "ink") {
			if (draftPoints.value.length > 1) {
				store.add({
					page: props.pageNumber,
					type: "ink",
					color: props.color ?? "#e03131",
					paths: [draftPoints.value.map(toBasePoint)],
					width: props.strokeWidth ?? 2,
				});
			}
		} else if ((tool === "square" || tool === "circle") && draftRect.value) {
			const rect = toBaseRect(draftRect.value);
			if (rect.w > 0.003 || rect.h > 0.003) {
				store.add({
					page: props.pageNumber,
					type: tool,
					color: props.color ?? "#e03131",
					rect,
					width: props.strokeWidth ?? 2,
				});
			}
		}

		draftPoints.value = [];
		draftStart.value = null;
		draftRect.value = null;
	}

	function createNote(point: Point) {
		const store = props.store;
		if (!store) return;
		const w = Math.min(0.32, 240 / Math.max(width.value, 1));
		const h = Math.min(0.12, 56 / Math.max(height.value, 1));
		const rect = toBaseRect({
			x: Math.min(point.x, 1 - w),
			y: Math.min(point.y, 1 - h),
			w,
			h,
		});
		const annotation = store.add({
			page: props.pageNumber,
			type: "note",
			color: props.color ?? "#ffd54a",
			rect,
			text: "",
			fontSize: 12,
		});
		beginEditing(annotation.id);
		store.tool.value = "cursor";
	}

	function placeImage(point: Point, source: string) {
		const store = props.store;
		if (!store) return;
		const image = new Image();
		image.onload = () => {
			const pageRatio = width.value / Math.max(height.value, 1);
			const targetW = 0.3;
			const targetH = (targetW * (image.naturalHeight / image.naturalWidth)) / pageRatio;
			const rect = toBaseRect({
				x: Math.min(point.x, 1 - targetW),
				y: Math.min(point.y, 1 - targetH),
				w: targetW,
				h: targetH,
			});
			store.add({
				page: props.pageNumber,
				type: "image",
				color: "#ffffff",
				rect,
				src: source,
			});
		};
		image.src = source;
		store.tool.value = "cursor";
	}

	function beginEditing(id: string) {
		const annotation = pageAnnotations.value.find((a) => a.id === id);
		if (!annotation || annotation.type !== "note") return;
		editingId.value = id;
		editingText.value = annotation.text;
		editingStyle.value = {
			left: `${annotation.rect.x * width.value}px`,
			top: `${annotation.rect.y * height.value}px`,
			width: `${Math.max(annotation.rect.w * width.value, 160)}px`,
			minHeight: `${Math.max(annotation.rect.h * height.value, 48)}px`,
		};
		nextTick(() => {
			const textarea = rootEl.value?.querySelector<HTMLTextAreaElement>(".pdf-note-editor");
			textarea?.focus();
		});
	}

	function commitEditing() {
		const id = editingId.value;
		if (!id || !props.store) return;
		const annotation = pageAnnotations.value.find((item) => item.id === id);
		const text = editingText.value.trim();
		if (annotation && annotation.type === "note") {
			if (!text) props.store.remove(id);
			else if (annotation.text !== editingText.value) {
				props.store.update(id, { text: editingText.value } as Partial<Annotation>);
			}
		}
		editingId.value = null;
		editingText.value = "";
	}

	function onTextSelection() {
		if (!isMarkupTool.value || !props.store) return;
		const selection = window.getSelection();
		const textContainer = textEl.value;
		if (!selection || selection.isCollapsed || !selection.rangeCount || !textContainer) return;
		const range = selection.getRangeAt(0);
		if (!textContainer.contains(range.commonAncestorContainer)) return;

		const box = rootEl.value?.getBoundingClientRect();
		if (!box) return;

		const rects: Rect[] = [];
		for (const client of Array.from(range.getClientRects())) {
			if (client.width < 2 || client.height < 2) continue;
			const rect = {
				x: (client.left - box.left) / box.width,
				y: (client.top - box.top) / box.height,
				w: client.width / box.width,
				h: client.height / box.height,
			};
			if (rect.x < -0.1 || rect.x > 1.1) continue;
			rects.push(toBaseRect(rect));
		}
		if (!rects.length) return;

		const text = selection.toString().trim();
		props.store.add({
			page: props.pageNumber,
			type: props.tool as "highlight" | "underline" | "strikeout",
			color: props.color ?? "#ffd54a",
			rects,
			text,
		});
		selection.removeAllRanges();
	}

	const inkDraftPath = computed(() => {
		if (props.tool !== "ink" || draftPoints.value.length < 2) return "";
		return draftPoints.value
			.map(
				(point, index) =>
					`${index === 0 ? "M" : "L"}${(point.x * width.value).toFixed(1)} ${(
						point.y * height.value
					).toFixed(1)}`
			)
			.join(" ");
	});

	const shapeDraft = computed(() => {
		const rect = draftRect.value;
		if (!rect || (props.tool !== "square" && props.tool !== "circle")) return null;
		return {
			x: rect.x * width.value,
			y: rect.y * height.value,
			w: rect.w * width.value,
			h: rect.h * height.value,
		};
	});

	watch(
		() => [props.doc, props.pageNumber],
		() => {
			rendered.value = false;
			loadPage();
		},
		{ immediate: true }
	);

	watch(
		() => [props.scale, props.extraRotation],
		() => {
			if (pageProxy.value) render();
		}
	);

	onBeforeUnmount(() => {
		renderToken += 1;
		renderTask?.cancel();
		textLayer?.cancel();
	});
</script>

<template>
	<div
		ref="rootEl"
		class="pdf-page"
		:data-page="pageNumber"
		:style="{ width: `${width}px`, height: `${height}px` }"
		@mouseup="onTextSelection">
		<canvas
			ref="canvasEl"
			class="pdf-canvas"
			:class="{ 'is-dark': dark, 'is-ready': rendered }"
			:aria-label="`Page ${pageNumber}`" />

		<div ref="textEl" class="textLayer" :class="{ 'is-markup': isMarkupTool }" />

		<div class="pdf-link-layer">
			<a
				v-for="(link, index) in links"
				:key="index"
				class="pdf-link"
				:href="link.url || '#'"
				:style="{
					left: `${link.x}px`,
					top: `${link.y}px`,
					width: `${link.w}px`,
					height: `${link.h}px`,
				}"
				@click.prevent="openLink(link)" />
		</div>

		<div class="pdf-search-layer">
			<div
				v-for="hit in results || []"
				:key="hit.index"
				class="pdf-search-hit"
				:class="{ 'is-active': hit.index === activeIndex }"
				:style="rectStyle(hit.rect)" />
		</div>

		<div class="pdf-annotation-layer">
			<AnnotationView
				v-for="annotation in pageAnnotations"
				:key="annotation.id"
				:annotation="annotation"
				:width="width"
				:height="height"
				:selected="annotation.id === selectedId"
				:interactive="interactiveAnnotations"
				@select="(id) => store?.select(id)"
				@edit="beginEditing" />
		</div>

		<svg
			v-if="inkDraftPath"
			class="pdf-draft"
			:viewBox="`0 0 ${width} ${height}`"
			preserveAspectRatio="none">
			<path
				:d="inkDraftPath"
				fill="none"
				:stroke="color || '#e03131'"
				:stroke-width="strokeWidth || 2"
				stroke-linecap="round"
				stroke-linejoin="round" />
		</svg>

		<svg
			v-else-if="shapeDraft"
			class="pdf-draft"
			:viewBox="`0 0 ${width} ${height}`"
			preserveAspectRatio="none">
			<rect
				v-if="tool === 'square'"
				:x="shapeDraft.x"
				:y="shapeDraft.y"
				:width="shapeDraft.w"
				:height="shapeDraft.h"
				fill="none"
				:stroke="color || '#e03131'"
				:stroke-width="strokeWidth || 2" />
			<ellipse
				v-else
				:cx="shapeDraft.x + shapeDraft.w / 2"
				:cy="shapeDraft.y + shapeDraft.h / 2"
				:rx="shapeDraft.w / 2"
				:ry="shapeDraft.h / 2"
				fill="none"
				:stroke="color || '#e03131'"
				:stroke-width="strokeWidth || 2" />
		</svg>

		<div
			v-if="isDrawTool"
			ref="surfaceEl"
			class="pdf-surface"
			:class="`is-${tool}`"
			@pointerdown.prevent="onPointerDown"
			@pointermove.prevent="onPointerMove"
			@pointerup.prevent="onPointerUp"
			@pointercancel="onPointerUp" />

		<textarea
			v-if="editingId"
			v-model="editingText"
			class="pdf-note-editor"
			autofocus
			:style="editingStyle"
			placeholder="Write a note, then click outside"
			@blur="commitEditing"
			@keydown.esc.prevent="commitEditing"
			@keydown.ctrl.enter.prevent="commitEditing" />

		<div v-if="!rendered" class="pdf-page-skeleton" />
	</div>
</template>

<style scoped>
	.pdf-page {
		position: relative;
		background: #fff;
		box-shadow:
			0 1px 3px rgba(0, 0, 0, 0.22),
			0 12px 28px rgba(0, 0, 0, 0.18);
		overflow: hidden;
		contain: layout paint;
	}

	.pdf-canvas {
		position: absolute;
		inset: 0;
		display: block;
		opacity: 0;
		transition: opacity 0.15s ease;
	}

	.pdf-canvas.is-ready {
		opacity: 1;
	}

	.pdf-canvas.is-dark {
		filter: invert(0.92) hue-rotate(180deg) contrast(0.92);
	}

	.pdf-page-skeleton {
		position: absolute;
		inset: 0;
		background: linear-gradient(100deg, #f2f2f2 30%, #fafafa 50%, #f2f2f2 70%);
		background-size: 200% 100%;
		animation: pdf-shimmer 1.4s linear infinite;
	}

	@keyframes pdf-shimmer {
		from {
			background-position: 150% 0;
		}
		to {
			background-position: -50% 0;
		}
	}

	.pdf-link-layer,
	.pdf-search-layer,
	.pdf-annotation-layer {
		position: absolute;
		inset: 0;
	}

	.pdf-link-layer {
		z-index: 3;
		pointer-events: none;
	}

	.pdf-link {
		position: absolute;
		pointer-events: auto;
		border-radius: 2px;
	}

	.pdf-link:hover {
		background: rgba(76, 110, 245, 0.14);
		outline: 1px solid rgba(76, 110, 245, 0.5);
	}

	.pdf-search-layer {
		z-index: 2;
		pointer-events: none;
	}

	.pdf-search-hit {
		position: absolute;
		background: rgba(255, 196, 0, 0.42);
		border-radius: 2px;
		mix-blend-mode: multiply;
	}

	.pdf-search-hit.is-active {
		background: rgba(255, 140, 0, 0.6);
		box-shadow: 0 0 0 2px rgba(255, 140, 0, 0.55);
	}

	.pdf-annotation-layer {
		z-index: 4;
		pointer-events: none;
	}

	.pdf-draft {
		position: absolute;
		inset: 0;
		z-index: 6;
		width: 100%;
		height: 100%;
		pointer-events: none;
	}

	.pdf-surface {
		position: absolute;
		inset: 0;
		z-index: 7;
		touch-action: none;
		cursor: crosshair;
	}

	.pdf-surface.is-note {
		cursor: copy;
	}

	.pdf-surface.is-image {
		cursor: cell;
	}

	.pdf-note-editor {
		position: absolute;
		z-index: 8;
		padding: 6px 8px;
		border: 1px solid #4c6ef5;
		border-radius: 6px;
		background: #fffbe8;
		box-shadow: 0 6px 18px rgba(0, 0, 0, 0.22);
		font: inherit;
		font-size: 12px;
		line-height: 1.4;
		color: #2b2b2b;
		resize: none;
		outline: none;
	}
</style>
