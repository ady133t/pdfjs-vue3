<script setup lang="ts">
	defineProps<{ name: string; size?: number }>();

	const paths: Record<string, string[]> = {
		thumbnails: ["M4 5h6v6H4zM14 5h6v6h-6zM4 15h6v4H4zM14 15h6v4h-6z"],
		outline: ["M4 6h16", "M4 12h10", "M4 18h7"],
		open: ["M3 7h6l2 2h10v10H3z", "M3 7V5h6l2 2"],
		prev: ["M15 5l-7 7 7 7"],
		next: ["M9 5l7 7-7 7"],
		minus: ["M5 12h14"],
		plus: ["M12 5v14", "M5 12h14"],
		fitWidth: ["M4 6v12", "M20 6v12", "M8 12h8", "M8 12l3-3", "M16 12l-3 3"],
		fitPage: ["M5 5h14v14H5z", "M9 12h6"],
		rotateCw: ["M20 12a8 8 0 1 1-2.3-5.7", "M20 4v5h-5"],
		rotateCcw: ["M4 12a8 8 0 1 0 2.3-5.7", "M4 4v5h5"],
		search: ["M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14z", "M16 16l4 4"],
		download: ["M12 4v11", "M8 11l4 4 4-4", "M5 20h14"],
		print: ["M7 8V4h10v4", "M7 17H5v-7h14v7h-2", "M7 14h10v6H7z"],
		fullscreen: ["M4 9V4h5", "M20 9V4h-5", "M4 15v5h5", "M20 15v5h-5"],
		exitFullscreen: ["M9 4v5H4", "M15 4v5h5", "M9 20v-5H4", "M15 20v-5h5"],
		theme: ["M12 3a9 9 0 1 0 9 9 7 7 0 0 1-9-9z"],
		info: ["M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z", "M12 11v5", "M12 8h.01"],
		highlight: ["M4 20h4l9-9-4-4-9 9z", "M14 4l4 4"],
		underline: ["M7 4v7a5 5 0 0 0 10 0V4", "M5 20h14"],
		strikeout: ["M4 12h16", "M8 8a4 4 0 0 1 8 0", "M8 16a4 4 0 0 0 8 0"],
		ink: ["M3 20c3 0 4-3 6-6s3-8 6-8 4 3 3 6-4 5-7 5-5 3-8 3z"],
		square: ["M4 5h16v14H4z"],
		circle: ["M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16z"],
		note: ["M5 4h9l5 5v11H5z", "M14 4v5h5", "M8 13h8", "M8 16h5"],
		image: ["M4 5h16v14H4z", "M4 15l4-4 4 4 3-3 5 5", "M15 8h.01"],
		cursor: ["M5 3l14 8-6 1-2 6z"],
		trash: ["M4 7h16", "M9 7V4h6v3", "M6 7l1 13h10l1-13"],
		undo: ["M4 10h10a5 5 0 0 1 0 10H9", "M4 10l4-4", "M4 10l4 4"],
		redo: ["M20 10H10a5 5 0 0 0 0 10h5", "M20 10l-4-4", "M20 10l-4 4"],
		close: ["M6 6l12 12", "M18 6L6 18"],
		save: ["M5 4h11l3 3v13H5z", "M8 4v6h7V4", "M8 20v-6h8v6"],
		menu: ["M4 7h16", "M4 12h16", "M4 17h16"],
		chevron: ["M9 6l6 6-6 6"],
		zoom: ["M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14z", "M16 16l4 4", "M8 11h6", "M11 8v6"],
	};

	function d(name: string): string[] {
		return paths[name] ?? paths.info;
	}
</script>

<template>
	<svg
		class="viewer-icon"
		:width="size || 18"
		:height="size || 18"
		viewBox="0 0 24 24"
		fill="none"
		stroke="currentColor"
		stroke-width="1.7"
		stroke-linecap="round"
		stroke-linejoin="round"
		aria-hidden="true">
		<path v-for="(path, index) in d(name)" :key="index" :d="path" />
	</svg>
</template>

<style scoped>
	.viewer-icon {
		display: block;
		flex: none;
	}
</style>
