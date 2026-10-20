<script setup lang="ts">
	import type { DocumentProperties } from "../lib/types";

	defineProps<{ properties: DocumentProperties }>();
	const emit = defineEmits<{ (e: "close"): void }>();

	function formatSize(bytes?: number): string {
		if (!bytes) return "—";
		const units = ["B", "KB", "MB", "GB"];
		let value = bytes;
		let unit = 0;
		while (value >= 1024 && unit < units.length - 1) {
			value /= 1024;
			unit += 1;
		}
		return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
	}
</script>

<template>
	<div class="pdf-modal-backdrop" @click.self="emit('close')">
		<div class="pdf-modal" role="dialog" aria-modal="true" aria-label="Document properties">
			<header class="pdf-modal-header">
				<h2 class="pdf-modal-title">Document properties</h2>
				<button class="pdf-modal-close" aria-label="Close" @click="emit('close')">×</button>
			</header>
			<dl class="pdf-property-list">
				<template v-if="properties.title">
					<dt>Title</dt>
					<dd>{{ properties.title }}</dd>
				</template>
				<template v-if="properties.author">
					<dt>Author</dt>
					<dd>{{ properties.author }}</dd>
				</template>
				<template v-if="properties.subject">
					<dt>Subject</dt>
					<dd>{{ properties.subject }}</dd>
				</template>
				<template v-if="properties.keywords">
					<dt>Keywords</dt>
					<dd>{{ properties.keywords }}</dd>
				</template>
				<template v-if="properties.creator">
					<dt>Creator</dt>
					<dd>{{ properties.creator }}</dd>
				</template>
				<template v-if="properties.producer">
					<dt>Producer</dt>
					<dd>{{ properties.producer }}</dd>
				</template>
				<template v-if="properties.creationDate">
					<dt>Created</dt>
					<dd>{{ properties.creationDate }}</dd>
				</template>
				<template v-if="properties.modificationDate">
					<dt>Modified</dt>
					<dd>{{ properties.modificationDate }}</dd>
				</template>
				<dt>Pages</dt>
				<dd>{{ properties.pages }}</dd>
				<dt>File size</dt>
				<dd>{{ formatSize(properties.fileSize) }}</dd>
			</dl>
			<footer class="pdf-modal-footer">
				<button class="pdf-modal-primary" @click="emit('close')">Close</button>
			</footer>
		</div>
	</div>
</template>

<style scoped>
	.pdf-modal-backdrop {
		position: absolute;
		inset: 0;
		z-index: 40;
		display: grid;
		place-items: center;
		background: rgba(15, 18, 25, 0.55);
		backdrop-filter: blur(2px);
		padding: 20px;
	}

	.pdf-modal {
		width: min(440px, 92vw);
		max-height: 80%;
		display: flex;
		flex-direction: column;
		background: var(--pdf-modal-bg, #fff);
		color: var(--pdf-fg, #2f3542);
		border-radius: 12px;
		box-shadow: 0 24px 60px rgba(0, 0, 0, 0.35);
		overflow: hidden;
	}

	.pdf-modal-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 14px 16px;
		border-bottom: 1px solid var(--pdf-border, #dcdfe6);
	}

	.pdf-modal-title {
		margin: 0;
		font-size: 15px;
		font-weight: 600;
	}

	.pdf-modal-close {
		border: none;
		background: transparent;
		color: inherit;
		font-size: 22px;
		line-height: 1;
		cursor: pointer;
	}

	.pdf-property-list {
		display: grid;
		grid-template-columns: 110px 1fr;
		gap: 6px 12px;
		margin: 0;
		padding: 16px;
		overflow: auto;
		font-size: 13px;
	}

	.pdf-property-list dt {
		color: var(--pdf-muted, #6b7280);
	}

	.pdf-property-list dd {
		margin: 0;
		word-break: break-word;
	}

	.pdf-modal-footer {
		display: flex;
		justify-content: flex-end;
		padding: 12px 16px;
		border-top: 1px solid var(--pdf-border, #dcdfe6);
	}

	.pdf-modal-primary {
		padding: 7px 16px;
		border: none;
		border-radius: 7px;
		background: var(--pdf-accent, #4c6ef5);
		color: #fff;
		font: inherit;
		font-size: 13px;
		cursor: pointer;
	}
</style>
