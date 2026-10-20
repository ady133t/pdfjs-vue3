<script setup lang="ts">
	import { ref } from "vue";

	interface OutlineNode {
		title: string;
		dest: unknown;
		items?: OutlineNode[];
	}

	const props = withDefaults(defineProps<{ items: OutlineNode[]; level?: number }>(), {
		level: 0,
	});

	const emit = defineEmits<{ (e: "navigate", dest: unknown): void }>();

	const open = ref<Record<number, boolean>>({});

	function toggle(index: number) {
		open.value = { ...open.value, [index]: !open.value[index] };
	}

	function isOpen(index: number): boolean {
		return open.value[index] ?? props.level === 0;
	}
</script>

<template>
	<ul class="pdf-outline-list" :class="`level-${level}`">
		<li v-for="(item, index) in items" :key="`${level}-${index}-${item.title}`" class="pdf-outline-item">
			<div class="pdf-outline-row">
				<button
					v-if="item.items?.length"
					class="pdf-outline-toggle"
					:aria-label="isOpen(index) ? 'Collapse section' : 'Expand section'"
					@click="toggle(index)">
					{{ isOpen(index) ? "▾" : "▸" }}
				</button>
				<span v-else class="pdf-outline-toggle is-empty" />
				<button class="pdf-outline-link" @click="emit('navigate', item.dest)">
					{{ item.title }}
				</button>
			</div>
			<PdfOutline
				v-if="item.items?.length && isOpen(index)"
				:items="item.items"
				:level="level + 1"
				@navigate="emit('navigate', $event)" />
		</li>
	</ul>
</template>

<style scoped>
	.pdf-outline-list {
		list-style: none;
		margin: 0;
		padding: 0;
	}

	.pdf-outline-list.level-1,
	.pdf-outline-list.level-2,
	.pdf-outline-list.level-3 {
		padding-left: 14px;
		border-left: 1px solid var(--pdf-border, #e1e4ea);
		margin-left: 10px;
	}

	.pdf-outline-row {
		display: flex;
		align-items: flex-start;
		gap: 2px;
	}

	.pdf-outline-toggle {
		width: 18px;
		height: 24px;
		flex: none;
		border: none;
		background: transparent;
		color: var(--pdf-muted, #6b7280);
		font-size: 10px;
		cursor: pointer;
	}

	.pdf-outline-toggle.is-empty {
		cursor: default;
	}

	.pdf-outline-link {
		flex: 1;
		padding: 4px 2px;
		border: none;
		background: transparent;
		color: var(--pdf-fg, #2f3542);
		font: inherit;
		font-size: 12px;
		text-align: left;
		line-height: 1.4;
		cursor: pointer;
		border-radius: 4px;
	}

	.pdf-outline-link:hover {
		color: var(--pdf-accent, #4c6ef5);
		background: var(--pdf-hover, #eef1f7);
	}
</style>
