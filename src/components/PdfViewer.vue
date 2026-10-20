<script setup lang="ts">
	import {
		computed,
		nextTick,
		onBeforeUnmount,
		onMounted,
		ref,
		shallowRef,
		watch,
	} from "vue";
	import * as pdfjsLib from "pdfjs-dist";
	import type { PDFDocumentProxy } from "pdfjs-dist";
	import workerUrl from "pdfjs-dist/build/pdf.worker.min.mjs?url";
	import PdfPage from "./PdfPage.vue";
	import PdfToolbar from "./PdfToolbar.vue";
	import PdfSidebar from "./PdfSidebar.vue";
	import PdfPropertiesDialog from "./PdfPropertiesDialog.vue";
	import PdfPasswordDialog from "./PdfPasswordDialog.vue";
	import type { ViewerPlugin } from "../plugins/annotation";
	import type {
		AnnotationTool,
		DocumentProperties,
		SearchResult,
		Theme,
		ToolbarOptions,
	} from "../lib/types";
	import "../styles/viewer.css";
	import { viewportRect } from "../lib/pdf-geometry";

	pdfjsLib.GlobalWorkerOptions.workerSrc = workerUrl;
	const pdfjsAssetBase = `${import.meta.env.BASE_URL}pdfjs/`;

	type PdfSource = string | Uint8Array | ArrayBuffer;

	const props = withDefaults(
		defineProps<{
			src?: PdfSource;
			/** Alias for `src`, kept for drop-in compatibility. */
			source?: PdfSource;
			plugins?: ViewerPlugin[];
			toolbarOptions?: ToolbarOptions | false;
			initialPage?: number;
			initialScale?: number | "auto" | "page-fit";
			theme?: Theme;
			height?: string | number;
			scrollMode?: "continuous" | "single";
			filename?: string;
		}>(),
		{
			plugins: () => [],
			toolbarOptions: () => ({}),
			initialPage: 1,
			initialScale: "auto",
			theme: "auto",
			height: "100%",
			scrollMode: "continuous",
		}
	);

	const emit = defineEmits<{
		(e: "loaded", doc: PDFDocumentProxy): void;
		(e: "loadProgress", payload: { loaded: number; total: number }): void;
		(e: "loadError", error: unknown): void;
		(e: "pageChanged", page: number): void;
		(e: "rotate", degrees: number): void;
		(e: "scroll", payload: { page: number; top: number }): void;
		(e: "update:darkMode", dark: boolean): void;
		(e: "passwordRequired", reason: number): void;
		(e: "passwordSuccess"): void;
		(e: "passwordError", reason: number): void;
		(e: "annotationCreated"): void;
	}>();

	const rootEl = ref<HTMLDivElement | null>(null);
	const viewportEl = ref<HTMLDivElement | null>(null);
	const pagesEl = ref<HTMLDivElement | null>(null);
	const toolbarRef = ref<InstanceType<typeof PdfToolbar> | null>(null);
	const openInput = ref<HTMLInputElement | null>(null);
	const imageInput = ref<HTMLInputElement | null>(null);

	const doc = shallowRef<PDFDocumentProxy | null>(null);
	const loading = ref(false);
	const progress = ref(0);
	const error = ref("");
	const pageCount = ref(0);
	const currentPage = ref(1);
	const scale = ref(1);
	const fitMode = ref<"none" | "width" | "page">("none");
	const rotation = ref(0);
	const sidebar = ref<string | null>(null);
	const isFullscreen = ref(false);
	const dragActive = ref(false);
	const toast = ref("");
	const propertiesOpen = ref(false);
	const properties = ref<DocumentProperties>({ pages: 0 });

	const passwordOpen = ref(false);
	const passwordError = ref("");
	let passwordCallback: ((password: string) => void) | null = null;
	let passwordNeeded = false;

	const sizes = ref<Record<number, { width: number; height: number; rotate: number }>>({});
	const mountedPages = ref<Set<number>>(new Set());

	const localSource = ref<{ data: Uint8Array; name: string; size: number } | null>(null);
	const pendingImage = ref<string | null>(null);

	const searchQuery = ref("");
	const searchResults = ref<SearchResult[]>([]);
	const searchIndex = ref(-1);
	const searching = ref(false);

	const userTheme = ref<Theme>(props.theme);
	const systemDark = ref(false);
	let mediaQuery: MediaQueryList | null = null;
	let toastTimer: number | undefined;
	let searchTimer: number | undefined;
	let searchToken = 0;
	let loadingTask: pdfjsLib.PDFDocumentLoadingTask | null = null;
	let pageObserver: IntersectionObserver | null = null;
	let resizeObserver: ResizeObserver | null = null;
	let scrollFrame = 0;

	const annotationPlugin = computed(() => props.plugins.find((plugin) => plugin.annotation)?.annotation);
	const annotationStore = computed(() => annotationPlugin.value?.store);
	const annotationTool = computed<AnnotationTool>(() => annotationStore.value?.tool.value ?? "cursor");
	const annotationColor = computed(() => annotationStore.value?.color.value ?? "#ffd54a");
	const annotationColors = computed(
		() => annotationPlugin.value?.options.colors ?? ["#ffd54a", "#8ce99a", "#74c0fc", "#ffa8a8"]
	);
	const annotationStroke = computed(() => annotationStore.value?.strokeWidth.value ?? 2);
	const selectedAnnotation = computed(() => annotationStore.value?.selectedId.value ?? null);
	const pageAnnotations = computed(() => annotationStore.value?.annotations ?? []);

	const effectiveSource = computed<PdfSource | undefined>(
		() => localSource.value?.data ?? props.src ?? props.source
	);

	const normalizedToolbar = computed<ToolbarOptions>(() =>
		props.toolbarOptions === false ? { show: false } : props.toolbarOptions
	);

	const isDark = computed(() => (userTheme.value === "auto" ? systemDark.value : userTheme.value === "dark"));
	const viewerStyle = computed(() => ({
		height: typeof props.height === "number" ? `${props.height}px` : props.height,
	}));
	const docKey = computed(() => {
		if (localSource.value) return `local:${localSource.value.name}:${localSource.value.size}`;
		const src = props.src ?? props.source;
		return typeof src === "string" ? src.slice(-160) : "document";
	});
	const visiblePages = computed(() =>
		props.scrollMode === "single" ? [currentPage.value] : Array.from({ length: pageCount.value }, (_, i) => i + 1)
	);

	function showToast(message: string) {
		toast.value = message;
		if (toastTimer) window.clearTimeout(toastTimer);
		toastTimer = window.setTimeout(() => {
			toast.value = "";
		}, 2200);
	}

	function clamp(value: number, min: number, max: number) {
		return Math.min(max, Math.max(min, value));
	}

	function displayBaseSize(page: number) {
		const size = sizes.value[page];
		if (!size) return null;
		const total = (size.rotate + rotation.value) % 360;
		return total % 180 === 0
			? { width: size.width, height: size.height }
			: { width: size.height, height: size.width };
	}

	function shellStyle(page: number) {
		const size = displayBaseSize(page) ?? { width: 612, height: 792 };
		return {
			width: `${Math.max(1, Math.floor(size.width * scale.value))}px`,
			height: `${Math.max(1, Math.floor(size.height * scale.value))}px`,
		};
	}

	function pageSizeFor(page: number) {
		const size = sizes.value[page];
		return size ? { width: size.width, height: size.height } : undefined;
	}

	function onPageReady(payload: { page: number; width: number; height: number; rotate: number }) {
		sizes.value = { ...sizes.value, [payload.page]: payload };
		if (fitMode.value === "none" && props.initialScale === "auto") {
			applyFit("width", false);
		} else if (fitMode.value !== "none") {
			applyFit(fitMode.value, false);
		}
	}

	function applyFit(mode: "width" | "page", scroll = false) {
		const viewport = viewportEl.value;
		const size = displayBaseSize(currentPage.value) ?? displayBaseSize(1);
		if (!viewport || !size) return;
		const padding = props.scrollMode === "single" ? 56 : 48;
		const availableWidth = Math.max(120, viewport.clientWidth - padding);
		const availableHeight = Math.max(120, viewport.clientHeight - padding);
		const next =
			mode === "width"
				? availableWidth / size.width
				: Math.min(availableWidth / size.width, availableHeight / size.height);
		scale.value = clamp(Number(next.toFixed(4)), 0.2, 6);
		fitMode.value = mode;
		if (scroll) nextTick(() => goToPage(currentPage.value, false));
	}

	function setZoom(next: number, mode: "none" | "width" | "page" = "none") {
		scale.value = clamp(Number(next.toFixed(3)), 0.2, 6);
		fitMode.value = mode;
	}

	function zoomIn(step = 0.25) {
		setZoom(scale.value + step);
	}

	function zoomOut(step = 0.25) {
		setZoom(scale.value - step);
	}

	function resetZoom() {
		setZoom(1);
	}

	function rotate(direction: 1 | -1) {
		rotation.value = (rotation.value + direction * 90 + 360) % 360;
		clearSearch();
		emit("rotate", rotation.value);
		if (fitMode.value !== "none") {
			nextTick(() => applyFit(fitMode.value === "page" ? "page" : "width", false));
		}
	}

	function setRotation(degrees: number) {
		rotation.value = ((degrees % 360) + 360) % 360;
		emit("rotate", rotation.value);
		clearSearch();
		if (fitMode.value !== "none") {
			nextTick(() => applyFit(fitMode.value === "page" ? "page" : "width", false));
		}
	}

	function goToPage(page: number, smooth = true) {
		if (!pageCount.value) return;
		const target = clamp(Math.round(page), 1, pageCount.value);
		currentPage.value = target;
		if (props.scrollMode === "single") {
			viewportEl.value?.scrollTo({ top: 0 });
			nextTick(() => {
				if (fitMode.value !== "none") applyFit(fitMode.value === "page" ? "page" : "width", false);
			});
			return;
		}
		const shell = pagesEl.value?.querySelector<HTMLElement>(`[data-page="${target}"]`);
		shell?.scrollIntoView({ behavior: smooth ? "smooth" : "auto", block: "start", inline: "nearest" });
	}

	function nextPage() {
		goToPage(currentPage.value + 1);
	}

	function previousPage() {
		goToPage(currentPage.value - 1);
	}

	function onScroll() {
		const viewport = viewportEl.value;
		if (!viewport) return;
		emit("scroll", { page: currentPage.value, top: viewport.scrollTop });
		if (props.scrollMode === "single" || scrollFrame) return;
		scrollFrame = window.requestAnimationFrame(() => {
			scrollFrame = 0;
			const containerTop = viewport.getBoundingClientRect().top;
			let best = currentPage.value;
			let bestDistance = Number.POSITIVE_INFINITY;
			for (const shell of Array.from(
				pagesEl.value?.querySelectorAll<HTMLElement>(".pdf-page-shell") ?? []
			)) {
				const rect = shell.getBoundingClientRect();
				const distance = Math.abs(rect.top - (containerTop + 8));
				if (rect.bottom > containerTop && distance < bestDistance) {
					bestDistance = distance;
					best = Number(shell.dataset.page);
				}
			}
			if (best && best !== currentPage.value) currentPage.value = best;
		});
	}

	function setupPageObserver() {
		pageObserver?.disconnect();
		const viewport = viewportEl.value;
		if (!viewport) return;
		pageObserver = new IntersectionObserver(
			(entries) => {
				let changed = false;
				const next = new Set(mountedPages.value);
				for (const entry of entries) {
					if (!entry.isIntersecting) continue;
					const page = Number((entry.target as HTMLElement).dataset.page);
					if (page && !next.has(page)) {
						next.add(page);
						changed = true;
					}
				}
				if (changed) mountedPages.value = next;
			},
			{ root: viewport, rootMargin: "700px 0px" }
		);
		for (const shell of Array.from(
			pagesEl.value?.querySelectorAll<HTMLElement>(".pdf-page-shell") ?? []
		)) {
			pageObserver.observe(shell);
		}
	}

	function isMounted(page: number) {
		return props.scrollMode === "single" || mountedPages.value.has(page);
	}

	function destroyDocument() {
		void loadingTask?.destroy();
		loadingTask = null;
		doc.value = null;
	}

	async function loadDocument() {
		const source = effectiveSource.value;
		if (!source) {
			destroyDocument();
			pageCount.value = 0;
			return;
		}

		destroyDocument();
		loading.value = true;
		error.value = "";
		progress.value = 0;
		searchQuery.value = "";
		searchResults.value = [];
		searchIndex.value = -1;
		sizes.value = {};
		mountedPages.value = new Set();
		fitMode.value = props.initialScale === "page-fit" ? "page" : props.initialScale === "auto" ? "width" : "none";
		rotation.value = 0;

		const options: Record<string, unknown> = {
			cMapUrl: `${pdfjsAssetBase}cmaps/`,
			cMapPacked: true,
			standardFontDataUrl: `${pdfjsAssetBase}standard_fonts/`,
			wasmUrl: `${pdfjsAssetBase}wasm/`,
		};
		if (typeof source === "string") options.url = source;
		else if (source instanceof ArrayBuffer) options.data = new Uint8Array(source.slice(0));
		else options.data = source.slice();

		const task = pdfjsLib.getDocument(options);
		loadingTask = task;
		task.onProgress = (payload: { loaded: number; total: number }) => {
			progress.value = payload.total ? payload.loaded / payload.total : 0;
			emit("loadProgress", { loaded: payload.loaded, total: payload.total });
		};
		task.onPassword = (updatePassword: (password: string) => void, reason: number) => {
			passwordCallback = updatePassword;
			passwordNeeded = true;
			passwordError.value =
				reason === pdfjsLib.PasswordResponses.INCORRECT_PASSWORD ? "Incorrect password. Try again." : "";
			passwordOpen.value = true;
			emit("passwordRequired", reason);
			if (passwordError.value) emit("passwordError", reason);
		};

		try {
			const loaded = await task.promise;
			if (loadingTask !== task) return;
			doc.value = loaded;
			pageCount.value = loaded.numPages;
			currentPage.value = clamp(props.initialPage, 1, loaded.numPages);
			loading.value = false;
			if (passwordNeeded) {
				passwordNeeded = false;
				emit("passwordSuccess");
			}
			emit("loaded", loaded);

			await nextTick();
			setupPageObserver();
			if (props.scrollMode === "continuous") {
				goToPage(currentPage.value, false);
			}

			if (annotationPlugin.value) {
				annotationPlugin.value.setPageSizeResolver((page) => pageSizeFor(page));
				annotationPlugin.value.restore(docKey.value);
			}
		} catch (loadError) {
			if (loadingTask !== task) return;
			loading.value = false;
			const name = (loadError as { name?: string })?.name;
			if (name === "PasswordException") {
				passwordOpen.value = true;
				return;
			}
			error.value = (loadError as Error)?.message || "The PDF could not be loaded.";
			emit("loadError", loadError);
		}
	}

	function submitPassword(password: string) {
		const callback = passwordCallback;
		if (!callback) return;
		passwordOpen.value = false;
		passwordError.value = "";
		callback(password);
	}

	function closePassword() {
		passwordOpen.value = false;
		passwordError.value = "";
		passwordNeeded = false;
		passwordCallback = null;
		loadingTask?.destroy();
		loadingTask = null;
		loading.value = false;
	}

	async function onFile(file: File | undefined) {
		if (!file) return;
		const data = new Uint8Array(await file.arrayBuffer());
		localSource.value = { data, name: file.name, size: file.size };
		fitMode.value = props.initialScale === "page-fit" ? "page" : "width";
	}

	function onFilePicked(event: Event) {
		const input = event.target as HTMLInputElement;
		void onFile(input.files?.[0]);
		input.value = "";
	}

	function onDragOver(event: DragEvent) {
		if (event.dataTransfer?.types.includes("Files")) dragActive.value = true;
	}

	function onDrop(event: DragEvent) {
		dragActive.value = false;
		const file = event.dataTransfer?.files?.[0];
		if (file && (file.type === "application/pdf" || file.name.endsWith(".pdf"))) {
			void onFile(file);
		} else {
			showToast("That does not look like a PDF.");
		}
	}

	function filename(): string {
		if (props.filename) return props.filename;
		if (localSource.value) return localSource.value.name;
		const source = props.src ?? props.source;
		if (typeof source === "string") {
			const clean = source.split("?")[0].split("#")[0];
			const name = clean.split("/").pop();
			if (name && name.toLowerCase().endsWith(".pdf")) return name;
		}
		return "document.pdf";
	}

	async function download() {
		if (!doc.value) return;
		const data = await doc.value.getData();
		const blob = new Blob([data as unknown as BlobPart], { type: "application/pdf" });
		const url = URL.createObjectURL(blob);
		const anchor = document.createElement("a");
		anchor.href = url;
		anchor.download = filename();
		document.body.append(anchor);
		anchor.click();
		anchor.remove();
		window.setTimeout(() => URL.revokeObjectURL(url), 4000);
	}

	async function print() {
		if (!doc.value) return;
		showToast("Preparing print preview…");
		const images: string[] = [];
		for (let pageNumber = 1; pageNumber <= doc.value.numPages; pageNumber += 1) {
			const page = await doc.value.getPage(pageNumber);
			const viewport = page.getViewport({ scale: 1.6 });
			const canvas = document.createElement("canvas");
			canvas.width = Math.floor(viewport.width);
			canvas.height = Math.floor(viewport.height);
			const context = canvas.getContext("2d");
			if (!context) continue;
			await page.render({ canvas, canvasContext: context, viewport }).promise;
			images.push(canvas.toDataURL("image/jpeg", 0.92));
		}

		const iframe = document.createElement("iframe");
		iframe.style.position = "fixed";
		iframe.style.right = "0";
		iframe.style.bottom = "0";
		iframe.style.width = "0";
		iframe.style.height = "0";
		iframe.style.border = "0";
		iframe.style.opacity = "0";
		iframe.srcdoc = `<!doctype html><html><head><meta charset="utf-8"><title>${filename()}</title>
<style>@page{margin:0}html,body{margin:0;padding:0}img{display:block;width:100%;page-break-after:always}img:last-child{page-break-after:auto}</style>
</head><body>${images.map((src) => `<img src="${src}" alt="">`).join("")}</body></html>`;
		document.body.append(iframe);
		await new Promise<void>((resolve) => {
			iframe.onload = () => resolve();
			window.setTimeout(resolve, 3000);
		});
		iframe.contentWindow?.focus();
		iframe.contentWindow?.print();
		window.setTimeout(() => iframe.remove(), 60000);
	}

	async function openProperties() {
		if (!doc.value) return;
		let info: Record<string, string> = {};
		let fileSize: number | undefined;
		try {
			const meta = await doc.value.getMetadata();
			info = (meta?.info ?? {}) as Record<string, string>;
		} catch {
			/* metadata is optional */
		}
		try {
			fileSize = (await doc.value.getData()).byteLength;
		} catch {
			fileSize = localSource.value?.size;
		}
		properties.value = {
			title: info.Title,
			author: info.Author,
			subject: info.Subject,
			keywords: info.Keywords,
			creator: info.Creator,
			producer: info.Producer,
			creationDate: formatPdfDate(info.CreationDate),
			modificationDate: formatPdfDate(info.ModDate),
			pages: doc.value.numPages,
			fileSize,
		};
		propertiesOpen.value = true;
	}

	function formatPdfDate(raw?: string): string | undefined {
		if (!raw) return undefined;
		const match = raw.match(/^D:(\d{4})(\d{2})?(\d{2})?(\d{2})?(\d{2})?(\d{2})?/);
		if (!match) return undefined;
		const [, year, month = "01", day = "01", hour = "00", minute = "00", second = "00"] = match;
		return `${year}-${month}-${day} ${hour}:${minute}:${second}`;
	}

	async function runSearch(query: string): Promise<SearchResult[]> {
		searchQuery.value = query;
		const needle = query.trim().toLowerCase();
		const token = ++searchToken;
		if (!needle || !doc.value) {
			searchResults.value = [];
			searchIndex.value = -1;
			searching.value = false;
			return [];
		}

		searching.value = true;
		const results: SearchResult[] = [];
		let index = 0;

		for (let pageNumber = 1; pageNumber <= doc.value.numPages; pageNumber += 1) {
			if (token !== searchToken) return results;
			const page = await doc.value.getPage(pageNumber);
			const viewport = page.getViewport({ scale: 1, rotation: page.rotate + rotation.value });
			const textContent = await page.getTextContent();
			for (const rawItem of textContent.items) {
				const item = rawItem as { str?: string; transform?: number[]; width?: number; height?: number };
				const str = item.str ?? "";
				if (!str || !item.transform) continue;
				const lower = str.toLowerCase();
				let from = 0;
				while (from <= lower.length) {
					const at = lower.indexOf(needle, from);
					if (at === -1) break;
					const [a, b, c, d, e, f] = item.transform;
					void a;
					void b;
					void c;
					void d;
					const width = item.width ?? 0;
					const startX = e + (width * at) / Math.max(str.length, 1);
					const endX = e + (width * (at + needle.length)) / Math.max(str.length, 1);
					const height = item.height ?? Math.abs(d) ?? 10;
					const rectPdf = [
						Math.min(startX, endX),
						f - height * 0.82,
						Math.max(startX, endX),
						f + height * 0.18,
					];
					const [x1, y1, x2, y2] = viewportRect(viewport, rectPdf as [number, number, number, number]);
					const rect = {
						x: Math.min(x1, x2) / viewport.width,
						y: Math.min(y1, y2) / viewport.height,
						w: Math.abs(x2 - x1) / viewport.width,
						h: Math.abs(y2 - y1) / viewport.height,
					};
					const snippetStart = Math.max(0, at - 28);
					const snippet = str.slice(snippetStart, Math.min(str.length, at + needle.length + 32)).trim();
					results.push({ page: pageNumber, index, rect, snippet });
					index += 1;
					from = at + Math.max(needle.length, 1);
				}
			}
		}

		if (token !== searchToken) return results;
		searchResults.value = results;
		searchIndex.value = results.length ? 0 : -1;
		searching.value = false;
		if (results.length) jumpToResult(0);
		return results;
	}

	function onSearchInput(query: string) {
		searchQuery.value = query;
		if (searchTimer) window.clearTimeout(searchTimer);
		if (!query.trim()) {
			searchResults.value = [];
			searchIndex.value = -1;
			return;
		}
		searchTimer = window.setTimeout(() => void runSearch(query), 260);
	}

	function jumpToResult(index: number) {
		const result = searchResults.value[index];
		if (!result) return;
		searchIndex.value = index;
		goToPage(result.page, true);
	}

	function searchNext() {
		if (!searchResults.value.length) return;
		jumpToResult((searchIndex.value + 1) % searchResults.value.length);
	}

	function searchPrevious() {
		if (!searchResults.value.length) return;
		jumpToResult((searchIndex.value - 1 + searchResults.value.length) % searchResults.value.length);
	}

	function clearSearch() {
		searchToken += 1;
		searchQuery.value = "";
		searchResults.value = [];
		searchIndex.value = -1;
	}

	function toggleSidebar(name: string) {
		sidebar.value = sidebar.value === name ? null : name;
		if (sidebar.value === "annotations" && annotationStore.value) {
			annotationStore.value.tool.value = "cursor";
		}
	}

	function deleteSelected() {
		const store = annotationStore.value;
		if (!store) return;
		const id = store.selectedId.value;
		if (id) store.remove(id);
	}

	function selectAnnotation(id: string) {
		const store = annotationStore.value;
		if (!store) return;
		const annotation = store.annotations.find((a) => a.id === id);
		if (!annotation) return;
		store.select(id);
		goToPage(annotation.page);
	}

	function exportAnnotations(format: "json" | "xfdf") {
		const store = annotationStore.value;
		if (!store) return;
		const text = format === "json" ? store.exportJSON() : store.exportXFDF();
		const blob = new Blob([text], {
			type: format === "json" ? "application/json" : "application/xml",
		});
		const url = URL.createObjectURL(blob);
		const anchor = document.createElement("a");
		anchor.href = url;
		anchor.download = `${filename().replace(/\.pdf$/i, "")}.${format}`;
		document.body.append(anchor);
		anchor.click();
		anchor.remove();
		window.setTimeout(() => URL.revokeObjectURL(url), 4000);
		showToast(`Exported ${store.annotations.length} annotation(s).`);
	}

	function importAnnotations(payload: { format: "json" | "xfdf"; text: string }) {
		const store = annotationStore.value;
		if (!store) return;
		try {
			const count =
				payload.format === "json"
					? store.importJSON(payload.text)
					: store.importXFDF(payload.text);
			showToast(`Imported ${count} annotation(s).`);
		} catch (importError) {
			console.error("[viewer] annotation import failed", importError);
			showToast("Could not import that annotation file.");
		}
	}

	function clearAnnotations() {
		const store = annotationStore.value;
		if (!store || !store.annotations.length) return;
		if (!window.confirm("Delete every annotation in this document?")) return;
		store.clear();
		showToast("Annotations cleared.");
	}

	function requestImage() {
		imageInput.value?.click();
	}

	function onImagePicked(event: Event) {
		const input = event.target as HTMLInputElement;
		const file = input.files?.[0];
		input.value = "";
		if (!file) return;
		const reader = new FileReader();
		reader.onload = () => {
			pendingImage.value = String(reader.result ?? "");
		};
		reader.readAsDataURL(file);
	}

	function navigateDest(dest: unknown) {
		if (!doc.value) return;
		Promise.resolve()
			.then(async () => {
				let target: unknown = dest;
				if (typeof dest === "string") {
					target = await doc.value?.getDestination(dest);
				}
				const explicit = target as unknown[] | null;
				if (!Array.isArray(explicit) || !explicit.length) return;
				const first = explicit[0];
				let pageIndex: number | null = null;
				if (typeof first === "number") pageIndex = first;
				else if (first && typeof first === "object" && doc.value) {
					pageIndex = await doc.value.getPageIndex(first as never);
				}
				if (pageIndex === null || pageIndex === undefined || pageIndex < 0) return;
				goToPage(pageIndex + 1);
			})
			.catch((navError) => console.warn("[viewer] could not resolve destination", navError));
	}

	async function toggleFullscreen() {
		try {
			if (!document.fullscreenElement) {
				await rootEl.value?.requestFullscreen();
				isFullscreen.value = true;
			} else {
				await document.exitFullscreen();
				isFullscreen.value = false;
			}
		} catch (fullscreenError) {
			console.warn("[viewer] fullscreen unavailable", fullscreenError);
			showToast("Full screen is not available here.");
		}
	}

	function toggleTheme() {
		userTheme.value = isDark.value ? "light" : "dark";
	}

	function focusViewport(event: PointerEvent) {
		const target = event.target as HTMLElement | null;
		if (target?.closest("input, textarea, select, button, a")) return;
		viewportEl.value?.focus({ preventScroll: true });
	}

	function onKeydown(event: KeyboardEvent) {
		const target = event.target as HTMLElement | null;
		if (target?.closest("input, textarea, select, [contenteditable='true']")) return;
		const meta = event.ctrlKey || event.metaKey;

		if (meta && event.key.toLowerCase() === "f") {
			event.preventDefault();
			toolbarRef.value?.focusSearch();
			return;
		}
		if (meta && event.key.toLowerCase() === "z" && annotationStore.value) {
			event.preventDefault();
			if (event.shiftKey) annotationStore.value.redo();
			else annotationStore.value.undo();
			return;
		}
		if (meta && event.key.toLowerCase() === "y" && annotationStore.value) {
			event.preventDefault();
			annotationStore.value.redo();
			return;
		}

		switch (event.key) {
			case "ArrowRight":
			case "PageDown":
				event.preventDefault();
				nextPage();
				break;
			case "ArrowLeft":
			case "PageUp":
				event.preventDefault();
				previousPage();
				break;
			case "+":
			case "=":
				event.preventDefault();
				zoomIn();
				break;
			case "-":
				event.preventDefault();
				zoomOut();
				break;
			case "0":
				event.preventDefault();
				resetZoom();
				break;
			case "Home":
				event.preventDefault();
				goToPage(1);
				break;
			case "End":
				event.preventDefault();
				goToPage(pageCount.value);
				break;
			case "Delete":
			case "Backspace":
				if (selectedAnnotation.value && annotationStore.value) {
					event.preventDefault();
					annotationStore.value.remove(selectedAnnotation.value);
				}
				break;
			case "Escape":
				if (passwordOpen.value) closePassword();
				else if (propertiesOpen.value) propertiesOpen.value = false;
				else sidebar.value = null;
				break;
			default:
				break;
		}
	}

	function onFullscreenChange() {
		isFullscreen.value = Boolean(document.fullscreenElement);
	}

	function onSystemThemeChange(event: MediaQueryListEvent) {
		systemDark.value = event.matches;
	}

	watch(effectiveSource, () => {
		void loadDocument();
	});

	watch(
		() => props.theme,
		(theme) => {
			userTheme.value = theme;
		}
	);

	watch(currentPage, (page) => {
		emit("pageChanged", page);
		if (props.scrollMode === "continuous") {
			const next = new Set(mountedPages.value);
			next.add(page);
			mountedPages.value = next;
		}
	});

	watch(isDark, (dark) => emit("update:darkMode", dark), { immediate: true });

	watch(pageCount, async () => {
		await nextTick();
		setupPageObserver();
	});

	watch(
		() => annotationStore.value?.annotations,
		() => {
			if (annotationPlugin.value?.options.autoSave) {
				annotationPlugin.value.persist(docKey.value);
			}
		},
		{ deep: true }
	);

	onMounted(() => {
		mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
		systemDark.value = mediaQuery.matches;
		mediaQuery.addEventListener("change", onSystemThemeChange);
		document.addEventListener("fullscreenchange", onFullscreenChange);
		resizeObserver = new ResizeObserver(() => {
			if (fitMode.value !== "none") applyFit(fitMode.value === "page" ? "page" : "width", false);
		});
		if (viewportEl.value) resizeObserver.observe(viewportEl.value);
		void loadDocument();
	});

	onBeforeUnmount(() => {
		document.removeEventListener("fullscreenchange", onFullscreenChange);
		mediaQuery?.removeEventListener("change", onSystemThemeChange);
		pageObserver?.disconnect();
		resizeObserver?.disconnect();
		if (toastTimer) window.clearTimeout(toastTimer);
		if (searchTimer) window.clearTimeout(searchTimer);
		if (scrollFrame) window.cancelAnimationFrame(scrollFrame);
		destroyDocument();
	});

	defineExpose({
		pageController: {
			goToPage,
			nextPage,
			previousPage,
			getPage: () => currentPage.value,
			getPageCount: () => pageCount.value,
		},
		zoomController: {
			zoomIn,
			zoomOut,
			setZoom,
			getZoom: () => scale.value,
			fitWidth: () => applyFit("width", true),
			fitPage: () => applyFit("page", true),
		},
		rotateController: {
			rotateClockwise: () => rotate(1),
			rotateCounterClockwise: () => rotate(-1),
			setRotation,
			getRotation: () => rotation.value,
		},
		searchController: {
			search: runSearch,
			next: searchNext,
			previous: searchPrevious,
			clear: clearSearch,
			getResults: () => searchResults.value,
		},
		downloadController: { download },
		printController: { print },
		annotationController: annotationPlugin.value?.controller,
		getDocument: () => doc.value,
	});
</script>

<template>
	<div
		ref="rootEl"
		class="pdf-viewer"
		:class="{ 'is-fullscreen': isFullscreen }"
		:data-theme="isDark ? 'dark' : 'light'"
		:style="viewerStyle"
		@keydown="onKeydown">
		<PdfToolbar
			v-if="normalizedToolbar.show !== false"
			ref="toolbarRef"
			:page="currentPage"
			:page-count="pageCount"
			:zoom="scale"
			:fit-mode="fitMode"
			:rotation="rotation"
			:sidebar="sidebar"
			:theme="userTheme"
			:dark="isDark"
			:options="normalizedToolbar"
			:has-annotation="Boolean(annotationPlugin)"
			:tool="annotationTool"
			:tools="annotationPlugin?.options.tools"
			:color="annotationColor"
			:colors="annotationColors"
			:can-undo="annotationStore?.canUndo.value ?? false"
			:can-redo="annotationStore?.canRedo.value ?? false"
			:has-selection="Boolean(selectedAnnotation)"
			:fullscreen="isFullscreen"
			:search-query="searchQuery"
			:search-count="searchResults.length"
			:search-index="searchIndex"
			@previous="previousPage"
			@next="nextPage"
			@go-to="goToPage"
			@zoom-in="zoomIn"
			@zoom-out="zoomOut"
			@reset-zoom="resetZoom"
			@fit="(mode) => applyFit(mode, true)"
			@rotate="rotate"
			@toggle-sidebar="toggleSidebar"
			@download="download"
			@print="print"
			@open="openInput?.click()"
			@fullscreen="toggleFullscreen"
			@theme="toggleTheme"
			@properties="openProperties"
			@search="onSearchInput"
			@search-next="searchNext"
			@search-previous="searchPrevious"
			@search-close="clearSearch"
			@set-tool="(tool) => (annotationStore!.tool.value = tool)"
			@set-color="(color) => (annotationStore!.color.value = color)"
			@set-stroke="(width) => (annotationStore!.strokeWidth.value = width)"
			@undo="annotationStore?.undo()"
			@redo="annotationStore?.redo()"
			@delete="deleteSelected" />

		<div class="pdf-viewer-body">
			<PdfSidebar
				v-if="doc && sidebar"
				:mode="sidebar"
				:doc="doc"
				:page="currentPage"
				:annotations="pageAnnotations"
				:selected-id="selectedAnnotation"
				:has-annotation="Boolean(annotationPlugin)"
				@close="sidebar = null"
				@switch="sidebar = $event"
				@go-to="goToPage"
				@navigate-dest="navigateDest"
				@select-annotation="selectAnnotation"
				@delete-annotation="(id) => annotationStore?.remove(id)"
				@clear-annotations="clearAnnotations"
				@export-annotations="exportAnnotations"
				@import-annotations="importAnnotations" />

			<div
				ref="viewportEl"
				class="pdf-viewport"
				tabindex="0"
				@pointerdown="focusViewport"
				@scroll.passive="onScroll"
				@dragover.prevent="onDragOver"
				@dragleave="dragActive = false"
				@drop.prevent="onDrop">
				<div v-if="loading" class="pdf-status">
					<div class="pdf-spinner" />
					<p class="pdf-status-title">Loading document…</p>
					<div class="pdf-progress-track">
						<div class="pdf-progress-value" :style="{ width: `${Math.round(progress * 100)}%` }" />
					</div>
				</div>

				<div v-else-if="error" class="pdf-status">
					<p class="pdf-status-title">Could not open this PDF</p>
					<p class="pdf-status-error">{{ error }}</p>
					<button class="pdf-text-button" @click="loadDocument">Try again</button>
				</div>

				<div v-else-if="!doc" class="pdf-status">
					<p class="pdf-status-title">No document loaded</p>
					<p>Drop a PDF here, or open one from the toolbar.</p>
				</div>

				<div v-else ref="pagesEl" class="pdf-pages">
					<div
						v-for="pageNumber in visiblePages"
						:key="pageNumber"
						class="pdf-page-shell"
						:data-page="pageNumber"
						:style="shellStyle(pageNumber)">
						<PdfPage
							v-if="isMounted(pageNumber)"
							:doc="doc"
							:page-number="pageNumber"
							:scale="scale"
							:extra-rotation="rotation"
							:dark="isDark"
							:tool="annotationTool"
							:color="annotationColor"
							:stroke-width="annotationStroke"
							:results="searchResults.filter((result) => result.page === pageNumber)"
							:active-index="searchIndex"
							:selected-id="selectedAnnotation"
							:store="annotationStore"
							:pending-image="pendingImage"
							@page-ready="onPageReady"
							@navigate="navigateDest"
							@request-image="requestImage" />
					</div>
				</div>

				<div v-if="dragActive" class="pdf-drop-zone">Drop the PDF to open it</div>
				<div v-if="toast" class="pdf-toast">{{ toast }}</div>
			</div>
		</div>

		<PdfPropertiesDialog
			v-if="propertiesOpen"
			:properties="properties"
			@close="propertiesOpen = false" />
		<PdfPasswordDialog
			v-if="passwordOpen"
			:error="passwordError"
			@submit="submitPassword"
			@close="closePassword" />

		<input ref="openInput" type="file" accept="application/pdf,.pdf" hidden @change="onFilePicked" />
		<input ref="imageInput" type="file" accept="image/*" hidden @change="onImagePicked" />
	</div>
</template>

<style scoped>
	.pdf-text-button {
		padding: 6px 12px;
		border: 1px solid var(--pdf-border, #dcdfe6);
		border-radius: 7px;
		background: var(--pdf-input-bg, #fff);
		color: inherit;
		font: inherit;
		font-size: 12px;
		cursor: pointer;
	}

	.pdf-text-button:hover {
		border-color: var(--pdf-accent, #4c6ef5);
		color: var(--pdf-accent, #4c6ef5);
	}
</style>
