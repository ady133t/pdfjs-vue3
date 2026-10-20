<script setup lang="ts">
	import { computed } from "vue";
	import type { Annotation } from "../lib/types";

	const props = defineProps<{
		annotation: Annotation;
		width: number;
		height: number;
		selected?: boolean;
		interactive?: boolean;
	}>();

	const emit = defineEmits<{
		(e: "select", id: string): void;
		(e: "edit", id: string): void;
	}>();

	const px = (value: number, total: number) => value * total;

	const rects = computed(() => {
		const a = props.annotation;
		if (a.type === "highlight" || a.type === "underline" || a.type === "strikeout") {
			return a.rects.map((r) => ({
				left: `${px(r.x, props.width)}px`,
				top: `${px(r.y, props.height)}px`,
				width: `${px(r.w, props.width)}px`,
				height: `${px(r.h, props.height)}px`,
			}));
		}
		return [];
	});

	const inkPath = computed(() => {
		const a = props.annotation;
		if (a.type !== "ink") return "";
		return a.paths
			.map((path) =>
				path
					.map(
						(point, index) =>
							`${index === 0 ? "M" : "L"}${px(point.x, props.width).toFixed(1)} ${px(
								point.y,
								props.height
							).toFixed(1)}`
					)
					.join(" ")
			)
			.join(" ");
	});

	const shape = computed(() => {
		const a = props.annotation;
		if (a.type !== "square" && a.type !== "circle") return null;
		return {
			x: px(a.rect.x, props.width),
			y: px(a.rect.y, props.height),
			w: px(a.rect.w, props.width),
			h: px(a.rect.h, props.height),
			strokeWidth: a.width,
			color: a.color,
		};
	});

	const note = computed(() => {
		const a = props.annotation;
		if (a.type !== "note") return null;
		return {
			left: `${px(a.rect.x, props.width)}px`,
			top: `${px(a.rect.y, props.height)}px`,
			width: `${px(a.rect.w, props.width)}px`,
			minHeight: `${px(a.rect.h, props.height)}px`,
			color: a.color,
			text: a.text,
			fontSize: `${a.fontSize}px`,
		};
	});

	const image = computed(() => {
		const a = props.annotation;
		if (a.type !== "image") return null;
		return {
			left: `${px(a.rect.x, props.width)}px`,
			top: `${px(a.rect.y, props.height)}px`,
			width: `${px(a.rect.w, props.width)}px`,
			height: `${px(a.rect.h, props.height)}px`,
			src: a.src,
		};
	});

	function select() {
		if (!props.interactive) return;
		emit("select", props.annotation.id);
	}

	function edit() {
		if (!props.interactive) return;
		emit("edit", props.annotation.id);
	}
</script>

<template>
	<div
		class="pdf-annotation"
		:class="[`is-${annotation.type}`, { 'is-selected': selected, 'is-interactive': interactive }]"
		:data-annotation-id="annotation.id"
		@pointerdown.stop="select"
		@dblclick.stop="edit">
		<template v-if="annotation.type === 'highlight'">
			<div
				v-for="(rect, index) in rects"
				:key="index"
				class="pdf-markup pdf-markup-highlight"
				:style="{ ...rect, backgroundColor: annotation.color }" />
		</template>

		<template v-else-if="annotation.type === 'underline'">
			<div
				v-for="(rect, index) in rects"
				:key="index"
				class="pdf-markup pdf-markup-underline"
				:style="{ ...rect, borderColor: annotation.color }" />
		</template>

		<template v-else-if="annotation.type === 'strikeout'">
			<div
				v-for="(rect, index) in rects"
				:key="index"
				class="pdf-markup pdf-markup-strikeout"
				:style="{ ...rect, '--strike-color': annotation.color }" />
		</template>

		<svg
			v-else-if="annotation.type === 'ink'"
			class="pdf-annotation-svg"
			:viewBox="`0 0 ${width} ${height}`"
			preserveAspectRatio="none">
			<path
				:d="inkPath"
				fill="none"
				:stroke="annotation.color"
				:stroke-width="annotation.width"
				stroke-linecap="round"
				stroke-linejoin="round" />
		</svg>

		<svg
			v-else-if="shape"
			class="pdf-annotation-svg"
			:viewBox="`0 0 ${width} ${height}`"
			preserveAspectRatio="none">
			<rect
				v-if="annotation.type === 'square'"
				:x="shape.x"
				:y="shape.y"
				:width="shape.w"
				:height="shape.h"
				fill="none"
				:stroke="shape.color"
				:stroke-width="shape.strokeWidth" />
			<ellipse
				v-else
				:cx="shape.x + shape.w / 2"
				:cy="shape.y + shape.h / 2"
				:rx="shape.w / 2"
				:ry="shape.h / 2"
				fill="none"
				:stroke="shape.color"
				:stroke-width="shape.strokeWidth" />
		</svg>

		<div v-else-if="note" class="pdf-note" :style="note">
			<svg class="pdf-note-icon" viewBox="0 0 24 24" aria-hidden="true">
				<path
					d="M5 3h9l5 5v13H5z"
					fill="none"
					stroke="currentColor"
					stroke-width="1.7"
					stroke-linejoin="round" />
				<path d="M14 3v5h5" fill="none" stroke="currentColor" stroke-width="1.7" />
			</svg>
			<span class="pdf-note-text">{{ note.text || "Note" }}</span>
		</div>

		<img
			v-else-if="image"
			class="pdf-image"
			:src="image.src"
			:style="image"
			alt="PDF annotation" />
	</div>
</template>

<style scoped>
	.pdf-annotation {
		position: absolute;
		inset: 0;
		pointer-events: none;
	}

	.pdf-annotation.is-interactive {
		pointer-events: auto;
	}

	.pdf-annotation.is-selected {
		outline: 1px dashed #4c6ef5;
		outline-offset: 2px;
		background: rgba(76, 110, 245, 0.04);
	}

	.pdf-markup {
		position: absolute;
		border-radius: 2px;
	}

	.pdf-markup-highlight {
		opacity: 0.38;
		mix-blend-mode: multiply;
	}

	.pdf-markup-underline {
		border-bottom: 2px solid;
		background: transparent;
	}

	.pdf-markup-strikeout {
		background: linear-gradient(
			to bottom,
			transparent calc(50% - 1px),
			var(--strike-color) calc(50% - 1px),
			var(--strike-color) calc(50% + 1px),
			transparent calc(50% + 1px)
		);
	}

	.pdf-annotation-svg {
		position: absolute;
		inset: 0;
		width: 100%;
		height: 100%;
		overflow: visible;
	}

	.pdf-note {
		position: absolute;
		display: flex;
		gap: 6px;
		align-items: flex-start;
		padding: 6px 8px;
		border-radius: 6px;
		background: #fff8d6;
		border: 1px solid #e6d38a;
		box-shadow: 0 2px 6px rgba(0, 0, 0, 0.14);
		color: #3d3520;
		font-size: 12px;
		line-height: 1.35;
		overflow: hidden;
	}

	.pdf-note-icon {
		width: 14px;
		height: 14px;
		flex: none;
		margin-top: 1px;
		color: #b08900;
	}

	.pdf-note-text {
		white-space: pre-wrap;
		word-break: break-word;
	}

	.pdf-image {
		position: absolute;
		object-fit: contain;
		border-radius: 2px;
	}
</style>
