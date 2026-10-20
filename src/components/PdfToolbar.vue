<script setup lang="ts">
	import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
	import ViewerIcon from "./ViewerIcon.vue";
	import type { AnnotationTool, ToolbarItem, ToolbarOptions } from "../lib/types";

	const props = withDefaults(
		defineProps<{
			page: number;
			pageCount: number;
			zoom: number;
			fitMode?: "none" | "width" | "page";
			rotation?: number;
			sidebar?: string | null;
			theme?: "light" | "dark" | "auto";
			dark?: boolean;
			options?: ToolbarOptions;
			hasAnnotation?: boolean;
			tool?: AnnotationTool;
			tools?: AnnotationTool[];
			color?: string;
			colors?: string[];
			canUndo?: boolean;
			canRedo?: boolean;
			hasSelection?: boolean;
			fullscreen?: boolean;
			officeExport?: boolean;
			exporting?: "docx" | "xlsx" | null;
			searchQuery?: string;
			searchCount?: number;
			searchIndex?: number;
		}>(),
		{
			fitMode: "none",
			rotation: 0,
			sidebar: null,
			theme: "auto",
			dark: false,
			options: () => ({}),
			hasAnnotation: false,
			tool: "cursor",
			color: "#ffd54a",
			colors: () => ["#ffd54a", "#8ce99a", "#74c0fc", "#ffa8a8", "#d0bfff", "#ffc078"],
			canUndo: false,
			canRedo: false,
			hasSelection: false,
			fullscreen: false,
			officeExport: false,
			exporting: null,
			searchQuery: "",
			searchCount: 0,
			searchIndex: -1,
		}
	);

	const emit = defineEmits<{
		(e: "previous"): void;
		(e: "next"): void;
		(e: "go-to", page: number): void;
		(e: "zoom-in"): void;
		(e: "zoom-out"): void;
		(e: "reset-zoom"): void;
		(e: "fit", mode: "width" | "page"): void;
		(e: "rotate", direction: 1 | -1): void;
		(e: "toggle-sidebar", name: string): void;
		(e: "download"): void;
		(e: "export-office", format: "docx" | "xlsx"): void;
		(e: "print"): void;
		(e: "open"): void;
		(e: "fullscreen"): void;
		(e: "theme"): void;
		(e: "properties"): void;
		(e: "search", query: string): void;
		(e: "search-next"): void;
		(e: "search-previous"): void;
		(e: "search-close"): void;
		(e: "set-tool", tool: AnnotationTool): void;
		(e: "set-color", color: string): void;
		(e: "set-stroke", width: number): void;
		(e: "undo"): void;
		(e: "redo"): void;
		(e: "delete"): void;
	}>();

	const ALL_ITEMS: ToolbarItem[] = [
		"thumbnails",
		"outline",
		"open",
		"pageNav",
		"zoom",
		"rotate",
		"search",
		"annotations",
		"download",
		"print",
		"fullscreen",
		"theme",
		"properties",
	];

	const ALLOWED_TOOLS: AnnotationTool[] = [
		"cursor",
		"highlight",
		"underline",
		"strikeout",
		"ink",
		"square",
		"circle",
		"note",
		"image",
	];

	const searchOpen = ref(false);
	const searchValue = ref(props.searchQuery);
	const pageValue = ref(String(props.page));
	const rootEl = ref<HTMLDivElement | null>(null);
	const exportMenuOpen = ref(false);
	const exportToggleEl = ref<HTMLButtonElement | null>(null);
	const exportMenuPosition = ref({ top: 0, right: 0 });

	watch(
		() => props.page,
		(value) => {
			pageValue.value = String(value);
		}
	);

	watch(
		() => props.searchQuery,
		(value) => {
			if (value !== undefined) searchValue.value = value;
		}
	);

	const allow = computed(() => new Set(props.options?.items ?? ALL_ITEMS));

	function on(item: ToolbarItem): boolean {
		if (props.options?.show === false) return false;
		if (!allow.value.has(item)) return false;
		const flag = props.options?.[item];
		return flag !== false;
	}

	const tools = computed<AnnotationTool[]>(() => {
		if (!props.hasAnnotation) return ["cursor"];
		const allowed = new Set(
			props.tools?.length ? props.tools : ALLOWED_TOOLS
		);
		return ["cursor", ...ALLOWED_TOOLS.filter((tool) => tool !== "cursor" && allowed.has(tool))];
	});

	const exportMenuStyle = computed<Record<string, string>>(() => ({
		top: `${exportMenuPosition.value.top}px`,
		right: `${exportMenuPosition.value.right}px`,
		"--pdf-toolbar-bg": props.dark ? "#23262e" : "#f8f9fb",
		"--pdf-fg": props.dark ? "#e6e9ef" : "#2f3542",
		"--pdf-muted": props.dark ? "#9aa3b2" : "#6b7280",
		"--pdf-border": props.dark ? "#343a46" : "#dcdfe6",
		"--pdf-hover": props.dark ? "#313743" : "#e9edf5",
		"--pdf-accent": props.dark ? "#7c93ff" : "#4c6ef5",
		"--pdf-accent-soft": props.dark ? "#2c3556" : "#e3eaff",
	}));

	function focusSearch() {
		searchOpen.value = true;
		requestAnimationFrame(() => {
			rootEl.value?.querySelector<HTMLInputElement>(".pdf-search-input")?.focus();
		});
	}

	defineExpose({ focusSearch });

	function commitPage() {
		const parsed = Number(pageValue.value);
		if (!Number.isFinite(parsed)) {
			pageValue.value = String(props.page);
			return;
		}
		emit("go-to", Math.min(props.pageCount, Math.max(1, Math.round(parsed))));
	}

	function onSearchInput() {
		emit("search", searchValue.value);
	}

	function closeSearch() {
		searchOpen.value = false;
		searchValue.value = "";
		emit("search", "");
		emit("search-close");
	}

	function toggleSidebar(name: string) {
		emit("toggle-sidebar", name);
	}

	function chooseExport(format: "docx" | "xlsx") {
		exportMenuOpen.value = false;
		emit("export-office", format);
	}

	function toggleExportMenu() {
		if (exportMenuOpen.value) {
			exportMenuOpen.value = false;
			return;
		}

		const rect = exportToggleEl.value?.getBoundingClientRect();
		if (!rect) return;
		exportMenuPosition.value = {
			top: Math.round(rect.bottom + 8),
			right: Math.max(8, Math.round(window.innerWidth - rect.right)),
		};
		exportMenuOpen.value = true;
	}

	function onDocumentPointerDown(event: PointerEvent) {
		if (!exportMenuOpen.value) return;
		const target = event.target as Element | null;
		if (
			target?.closest(".pdf-download-control") ||
			target?.closest(".pdf-export-menu")
		) {
			return;
		}
		exportMenuOpen.value = false;
	}

	function onDocumentKeydown(event: KeyboardEvent) {
		if (event.key === "Escape") exportMenuOpen.value = false;
	}

	function onViewportChange() {
		if (exportMenuOpen.value) exportMenuOpen.value = false;
	}

	onMounted(() => {
		document.addEventListener("pointerdown", onDocumentPointerDown);
		document.addEventListener("keydown", onDocumentKeydown);
		window.addEventListener("resize", onViewportChange);
		window.addEventListener("scroll", onViewportChange, true);
	});

	onBeforeUnmount(() => {
		document.removeEventListener("pointerdown", onDocumentPointerDown);
		document.removeEventListener("keydown", onDocumentKeydown);
		window.removeEventListener("resize", onViewportChange);
		window.removeEventListener("scroll", onViewportChange, true);
	});

	watch(
		() => props.exporting,
		(value) => {
			if (value) exportMenuOpen.value = false;
		}
	);
</script>

<template>
	<div ref="rootEl" class="pdf-toolbar" role="toolbar" aria-label="PDF viewer toolbar">
		<div class="pdf-toolbar-row">
			<div class="pdf-toolbar-group">
				<button
					v-if="on('thumbnails')"
					class="pdf-tool-button"
					:class="{ 'is-active': sidebar === 'thumbnails' }"
					title="Thumbnails"
					aria-label="Toggle thumbnails"
					@click="toggleSidebar('thumbnails')">
					<ViewerIcon name="thumbnails" />
				</button>
				<button
					v-if="on('outline')"
					class="pdf-tool-button"
					:class="{ 'is-active': sidebar === 'outline' }"
					title="Table of contents"
					aria-label="Toggle table of contents"
					@click="toggleSidebar('outline')">
					<ViewerIcon name="outline" />
				</button>
				<button
					v-if="on('open')"
					class="pdf-tool-button"
					title="Open a local PDF"
					aria-label="Open a local PDF"
					@click="emit('open')">
					<ViewerIcon name="open" />
				</button>
			</div>

			<div v-if="on('pageNav')" class="pdf-toolbar-group">
				<button
					class="pdf-tool-button"
					:disabled="page <= 1"
					title="Previous page"
					aria-label="Previous page"
					@click="emit('previous')">
					<ViewerIcon name="prev" />
				</button>
				<form class="pdf-page-form" @submit.prevent="commitPage">
					<input
						v-model="pageValue"
						class="pdf-page-input"
						type="text"
						inputmode="numeric"
						aria-label="Page number"
						@blur="commitPage"
						@keydown.enter.prevent="commitPage" />
					<span class="pdf-page-total">/ {{ pageCount || "–" }}</span>
				</form>
				<button
					class="pdf-tool-button"
					:disabled="!pageCount || page >= pageCount"
					title="Next page"
					aria-label="Next page"
					@click="emit('next')">
					<ViewerIcon name="next" />
				</button>
			</div>

			<div v-if="on('zoom')" class="pdf-toolbar-group">
				<button
					class="pdf-tool-button"
					:disabled="zoom <= 0.25"
					title="Zoom out"
					aria-label="Zoom out"
					@click="emit('zoom-out')">
					<ViewerIcon name="minus" />
				</button>
				<button
					class="pdf-zoom-value"
					title="Reset zoom to 100%"
					@click="emit('reset-zoom')">
					{{ Math.round(zoom * 100) }}%
				</button>
				<button
					class="pdf-tool-button"
					:disabled="zoom >= 6"
					title="Zoom in"
					aria-label="Zoom in"
					@click="emit('zoom-in')">
					<ViewerIcon name="plus" />
				</button>
				<button
					class="pdf-tool-button"
					:class="{ 'is-active': fitMode === 'width' }"
					title="Fit width"
					aria-label="Fit width"
					@click="emit('fit', 'width')">
					<ViewerIcon name="fitWidth" />
				</button>
				<button
					class="pdf-tool-button"
					:class="{ 'is-active': fitMode === 'page' }"
					title="Fit page"
					aria-label="Fit page"
					@click="emit('fit', 'page')">
					<ViewerIcon name="fitPage" />
				</button>
			</div>

			<div v-if="on('rotate')" class="pdf-toolbar-group">
				<button
					class="pdf-tool-button"
					title="Rotate counter-clockwise"
					aria-label="Rotate counter-clockwise"
					@click="emit('rotate', -1)">
					<ViewerIcon name="rotateCcw" />
				</button>
				<button
					class="pdf-tool-button"
					title="Rotate clockwise"
					aria-label="Rotate clockwise"
					@click="emit('rotate', 1)">
					<ViewerIcon name="rotateCw" />
				</button>
			</div>

			<div class="pdf-toolbar-spacer" />

			<form
				v-if="searchOpen"
				class="pdf-search-box"
				@submit.prevent="emit('search-next')">
				<ViewerIcon name="search" :size="15" />
				<input
					v-model="searchValue"
					class="pdf-search-input"
					type="search"
					placeholder="Search document"
					aria-label="Search document"
					@input="onSearchInput" />
				<span v-if="searchValue" class="pdf-search-count">
					{{ searchCount ? `${searchIndex + 1}/${searchCount}` : "0/0" }}
				</span>
				<button
					v-if="searchValue"
					type="button"
					class="pdf-tool-button is-tiny"
					title="Previous match"
					aria-label="Previous match"
					@click="emit('search-previous')">
					<ViewerIcon name="prev" :size="14" />
				</button>
				<button
					v-if="searchValue"
					type="button"
					class="pdf-tool-button is-tiny"
					title="Next match"
					aria-label="Next match"
					@click="emit('search-next')">
					<ViewerIcon name="next" :size="14" />
				</button>
				<button
					type="button"
					class="pdf-tool-button is-tiny"
					title="Close search"
					aria-label="Close search"
					@click="closeSearch">
					<ViewerIcon name="close" :size="14" />
				</button>
			</form>

			<div class="pdf-toolbar-group">
				<button
					v-if="on('search') && !searchOpen"
					class="pdf-tool-button"
					title="Search"
					aria-label="Search"
					@click="searchOpen = true">
					<ViewerIcon name="search" />
				</button>
				<button
					v-if="hasAnnotation && on('annotations')"
					class="pdf-tool-button"
					:class="{ 'is-active': sidebar === 'annotations' }"
					title="Annotations"
					aria-label="Toggle annotations"
					@click="toggleSidebar('annotations')">
					<ViewerIcon name="highlight" />
				</button>
				<div
					v-if="on('download')"
					class="pdf-download-control"
					:class="{ 'is-exporting': exporting }">
					<button
						class="pdf-tool-button"
						title="Download PDF"
						aria-label="Download PDF"
						:disabled="Boolean(exporting)"
						@click="emit('download')">
						<span v-if="exporting" class="pdf-export-spinner" aria-hidden="true" />
						<ViewerIcon v-else name="download" />
					</button>
					<button
						v-if="officeExport"
						ref="exportToggleEl"
						type="button"
						class="pdf-tool-button pdf-export-toggle"
						:class="{ 'is-active': exportMenuOpen }"
						title="Convert to Word or Excel"
						aria-label="Convert to Word or Excel"
						aria-haspopup="menu"
						:aria-expanded="exportMenuOpen"
						:disabled="Boolean(exporting)"
						@click.stop="toggleExportMenu">
						<span aria-hidden="true">▾</span>
					</button>
					<Teleport to="body">
						<div
							v-if="officeExport && exportMenuOpen"
							class="pdf-export-menu"
							:style="exportMenuStyle"
							role="menu"
							aria-label="Convert document"
							@pointerdown.stop>
							<p class="pdf-export-title">Convert document</p>
							<button
								type="button"
								role="menuitem"
								:disabled="Boolean(exporting)"
								@click="chooseExport('docx')">
								<span class="pdf-export-ext">DOCX</span>
								<span class="pdf-export-label">Word document</span>
							</button>
							<button
								type="button"
								role="menuitem"
								:disabled="Boolean(exporting)"
								@click="chooseExport('xlsx')">
								<span class="pdf-export-ext">XLSX</span>
								<span class="pdf-export-label">Excel workbook</span>
							</button>
							<p class="pdf-export-hint">
								Tables are detected for Excel. Conversion runs on the
								self-hosted service.
							</p>
						</div>
					</Teleport>
				</div>
				<button
					v-if="on('print')"
					class="pdf-tool-button"
					title="Print"
					aria-label="Print PDF"
					@click="emit('print')">
					<ViewerIcon name="print" />
				</button>
				<button
					v-if="on('fullscreen')"
					class="pdf-tool-button"
					:title="fullscreen ? 'Exit full screen' : 'Full screen'"
					:aria-label="fullscreen ? 'Exit full screen' : 'Full screen'"
					@click="emit('fullscreen')">
					<ViewerIcon :name="fullscreen ? 'exitFullscreen' : 'fullscreen'" />
				</button>
				<button
					v-if="on('theme')"
					class="pdf-tool-button"
					:title="dark ? 'Switch to light theme' : 'Switch to dark theme'"
					:aria-label="dark ? 'Switch to light theme' : 'Switch to dark theme'"
					@click="emit('theme')">
					<ViewerIcon name="theme" />
				</button>
				<button
					v-if="on('properties')"
					class="pdf-tool-button"
					title="Document properties"
					aria-label="Document properties"
					@click="emit('properties')">
					<ViewerIcon name="info" />
				</button>
			</div>
		</div>

		<div v-if="hasAnnotation && on('annotations')" class="pdf-toolbar-row is-annotation">
			<div class="pdf-toolbar-group">
				<button
					v-for="availableTool in tools"
					:key="availableTool"
					class="pdf-tool-button"
					:class="{ 'is-active': tool === availableTool }"
					:title="availableTool"
					:aria-label="availableTool"
					@click="emit('set-tool', availableTool)">
					<ViewerIcon :name="availableTool" />
				</button>
			</div>

			<div class="pdf-toolbar-separator" />

			<div class="pdf-toolbar-group pdf-color-group">
				<button
					v-for="swatch in colors"
					:key="swatch"
					class="pdf-swatch"
					:class="{ 'is-active': color === swatch }"
					:style="{ backgroundColor: swatch }"
					:title="`Colour ${swatch}`"
					:aria-label="`Colour ${swatch}`"
					@click="emit('set-color', swatch)" />
			</div>

			<div class="pdf-toolbar-separator" />

			<label class="pdf-stroke-control">
				<span class="pdf-stroke-label">Stroke</span>
				<input
					class="pdf-stroke-input"
					type="range"
					min="1"
					max="6"
					step="1"
					:value="2"
					aria-label="Stroke width"
					@input="emit('set-stroke', Number(($event.target as HTMLInputElement).value))" />
			</label>

			<div class="pdf-toolbar-spacer" />

			<div class="pdf-toolbar-group">
				<button
					class="pdf-tool-button"
					:disabled="!canUndo"
					title="Undo"
					aria-label="Undo"
					@click="emit('undo')">
					<ViewerIcon name="undo" />
				</button>
				<button
					class="pdf-tool-button"
					:disabled="!canRedo"
					title="Redo"
					aria-label="Redo"
					@click="emit('redo')">
					<ViewerIcon name="redo" />
				</button>
				<button
					class="pdf-tool-button is-danger"
					:disabled="!hasSelection"
					title="Delete selected annotation"
					aria-label="Delete selected annotation"
					@click="emit('delete')">
					<ViewerIcon name="trash" />
				</button>
			</div>
		</div>
	</div>
</template>

<style scoped>
	.pdf-toolbar {
		display: flex;
		flex-direction: column;
		background: var(--pdf-toolbar-bg, #f8f9fb);
		border-bottom: 1px solid var(--pdf-border, #dcdfe6);
		color: var(--pdf-fg, #2f3542);
		font-size: 13px;
		user-select: none;
	}

	.pdf-toolbar-row {
		display: flex;
		align-items: center;
		gap: 8px;
		min-height: 44px;
		padding: 6px 10px;
		flex-wrap: wrap;
	}

	.pdf-toolbar-row.is-annotation {
		border-top: 1px solid var(--pdf-border, #dcdfe6);
		min-height: 40px;
		padding-top: 4px;
		padding-bottom: 4px;
	}

	.pdf-toolbar-group {
		display: flex;
		align-items: center;
		gap: 2px;
	}

	.pdf-toolbar-spacer {
		flex: 1;
	}

	.pdf-toolbar-separator {
		width: 1px;
		height: 22px;
		background: var(--pdf-border, #dcdfe6);
	}

	.pdf-tool-button {
		display: inline-flex;
		align-items: center;
		justify-content: center;
		width: 32px;
		height: 32px;
		padding: 0;
		border: 1px solid transparent;
		border-radius: 7px;
		background: transparent;
		color: inherit;
		font: inherit;
		cursor: pointer;
		transition:
			background-color 0.14s ease,
			border-color 0.14s ease;
	}

	.pdf-tool-button.is-tiny {
		width: 24px;
		height: 24px;
	}

	.pdf-tool-button:hover:not(:disabled) {
		background: var(--pdf-hover, #e9edf5);
	}

	.pdf-tool-button.is-active {
		background: var(--pdf-accent-soft, #dfe7ff);
		border-color: var(--pdf-accent, #4c6ef5);
		color: var(--pdf-accent, #4c6ef5);
	}

	.pdf-tool-button.is-danger:hover:not(:disabled) {
		background: #ffe3e3;
		color: #c92a2a;
	}

	.pdf-tool-button:disabled {
		opacity: 0.38;
		cursor: not-allowed;
	}

	.pdf-download-control {
		position: relative;
		display: inline-flex;
		align-items: center;
	}

	.pdf-download-control > .pdf-tool-button:first-child {
		border-top-right-radius: 4px;
		border-bottom-right-radius: 4px;
	}

	.pdf-export-toggle {
		width: 18px;
		margin-left: -2px;
		border-top-left-radius: 4px;
		border-bottom-left-radius: 4px;
		font-size: 10px;
		color: var(--pdf-muted, #6b7280);
	}

	.pdf-export-menu {
		position: fixed;
		top: 0;
		right: 0;
		z-index: 1000;
		width: 248px;
		max-width: calc(100vw - 16px);
		box-sizing: border-box;
		padding: 8px;
		border: 1px solid var(--pdf-border, #dcdfe6);
		border-radius: 10px;
		background: var(--pdf-toolbar-bg, #f8f9fb);
		color: var(--pdf-fg, #2f3542);
		font-family:
			Inter,
			ui-sans-serif,
			system-ui,
			-apple-system,
			"Segoe UI",
			Roboto,
			sans-serif;
		font-size: 13px;
		box-shadow:
			0 10px 24px rgba(15, 23, 42, 0.16),
			0 2px 6px rgba(15, 23, 42, 0.12);
	}

	.pdf-export-title {
		margin: 2px 6px 6px;
		font-size: 10px;
		font-weight: 700;
		letter-spacing: 0.08em;
		text-transform: uppercase;
		color: var(--pdf-muted, #6b7280);
	}

	.pdf-export-menu button {
		display: flex;
		align-items: center;
		gap: 10px;
		width: 100%;
		padding: 8px;
		border: 0;
		border-radius: 7px;
		background: transparent;
		color: var(--pdf-fg, #2f3542);
		font: inherit;
		text-align: left;
		cursor: pointer;
	}

	.pdf-export-menu button:hover:not(:disabled) {
		background: var(--pdf-hover, #e9edf5);
	}

	.pdf-export-menu button:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.pdf-export-ext {
		display: inline-flex;
		justify-content: center;
		width: 44px;
		padding: 3px 0;
		border-radius: 4px;
		background: var(--pdf-accent-soft, #dfe7ff);
		color: var(--pdf-accent, #4c6ef5);
		font-size: 9px;
		font-weight: 700;
		letter-spacing: 0.04em;
	}

	.pdf-export-label {
		font-size: 12px;
		font-weight: 600;
	}

	.pdf-export-hint {
		margin: 6px 6px 2px;
		color: var(--pdf-muted, #6b7280);
		font-size: 10px;
		line-height: 1.45;
	}

	.pdf-export-spinner {
		width: 14px;
		height: 14px;
		border: 2px solid currentColor;
		border-right-color: transparent;
		border-radius: 50%;
		animation: pdf-export-spin 0.7s linear infinite;
	}

	@keyframes pdf-export-spin {
		to {
			transform: rotate(360deg);
		}
	}

	.pdf-page-form {
		display: flex;
		align-items: center;
		gap: 4px;
		padding: 0 2px;
	}

	.pdf-page-input {
		width: 42px;
		height: 28px;
		padding: 0 4px;
		border: 1px solid var(--pdf-border, #dcdfe6);
		border-radius: 6px;
		background: var(--pdf-input-bg, #fff);
		color: inherit;
		font: inherit;
		text-align: center;
		outline: none;
	}

	.pdf-page-input:focus {
		border-color: var(--pdf-accent, #4c6ef5);
	}

	.pdf-page-total {
		min-width: 42px;
		color: var(--pdf-muted, #6b7280);
	}

	.pdf-zoom-value {
		min-width: 52px;
		height: 28px;
		padding: 0 6px;
		border: 1px solid transparent;
		border-radius: 6px;
		background: transparent;
		color: inherit;
		font: inherit;
		cursor: pointer;
	}

	.pdf-zoom-value:hover {
		background: var(--pdf-hover, #e9edf5);
	}

	.pdf-search-box {
		display: flex;
		align-items: center;
		gap: 6px;
		height: 32px;
		padding: 0 6px 0 8px;
		border: 1px solid var(--pdf-border, #dcdfe6);
		border-radius: 8px;
		background: var(--pdf-input-bg, #fff);
		color: var(--pdf-muted, #6b7280);
	}

	.pdf-search-box:focus-within {
		border-color: var(--pdf-accent, #4c6ef5);
	}

	.pdf-search-input {
		width: 170px;
		border: none;
		background: transparent;
		color: var(--pdf-fg, #2f3542);
		font: inherit;
		outline: none;
	}

	.pdf-search-count {
		font-size: 11px;
		white-space: nowrap;
	}

	.pdf-swatch {
		width: 18px;
		height: 18px;
		padding: 0;
		border: 2px solid transparent;
		border-radius: 50%;
		cursor: pointer;
		box-shadow: inset 0 0 0 1px rgba(0, 0, 0, 0.18);
	}

	.pdf-swatch.is-active {
		border-color: var(--pdf-accent, #4c6ef5);
		transform: scale(1.12);
	}

	.pdf-color-group {
		gap: 5px;
		padding: 0 2px;
	}

	.pdf-stroke-control {
		display: flex;
		align-items: center;
		gap: 6px;
		color: var(--pdf-muted, #6b7280);
		font-size: 11px;
		text-transform: uppercase;
		letter-spacing: 0.04em;
	}

	.pdf-stroke-input {
		width: 70px;
		accent-color: var(--pdf-accent, #4c6ef5);
	}

	@container pdfviewer (max-width: 720px) {
		.pdf-toolbar-row {
			flex-wrap: nowrap;
			overflow-x: auto;
			overscroll-behavior-x: contain;
			scrollbar-width: none;
			gap: 2px;
			padding: 5px 6px;
			mask-image: linear-gradient(to right, #000 calc(100% - 22px), transparent);
		}

		.pdf-toolbar-row::-webkit-scrollbar {
			display: none;
		}

		.pdf-toolbar-group,
		.pdf-toolbar-separator,
		.pdf-stroke-control,
		.pdf-search-box {
			flex: 0 0 auto;
		}

		.pdf-toolbar-spacer {
			flex: 0 0 0;
		}

		.pdf-search-input {
			width: 110px;
		}

		.pdf-stroke-label {
			display: none;
		}
	}
</style>
