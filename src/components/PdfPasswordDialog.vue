<script setup lang="ts">
	import { nextTick, onMounted, ref } from "vue";

	const props = defineProps<{ error?: string }>();
	const emit = defineEmits<{
		(e: "submit", password: string): void;
		(e: "close"): void;
	}>();

	const password = ref("");
	const input = ref<HTMLInputElement | null>(null);

	onMounted(async () => {
		await nextTick();
		input.value?.focus();
	});

	function submit() {
		if (!password.value) return;
		emit("submit", password.value);
	}
</script>

<template>
	<div class="pdf-modal-backdrop" @click.self="emit('close')">
		<form class="pdf-modal" role="dialog" aria-modal="true" aria-label="Password required" @submit.prevent="submit">
			<header class="pdf-modal-header">
				<h2 class="pdf-modal-title">This document is protected</h2>
				<button type="button" class="pdf-modal-close" aria-label="Close" @click="emit('close')">×</button>
			</header>
			<div class="pdf-modal-body">
				<p class="pdf-modal-copy">Enter the password to open this PDF.</p>
				<input
					ref="input"
					v-model="password"
					class="pdf-modal-input"
					type="password"
					autocomplete="off"
					placeholder="Password"
					aria-label="Password" />
				<p v-if="props.error" class="pdf-modal-error">{{ props.error }}</p>
			</div>
			<footer class="pdf-modal-footer">
				<button type="button" class="pdf-modal-secondary" @click="emit('close')">Cancel</button>
				<button type="submit" class="pdf-modal-primary" :disabled="!password">Unlock</button>
			</footer>
		</form>
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
		width: min(390px, 92vw);
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

	.pdf-modal-body {
		padding: 16px;
	}

	.pdf-modal-copy {
		margin: 0 0 10px;
		font-size: 13px;
		color: var(--pdf-muted, #6b7280);
	}

	.pdf-modal-input {
		width: 100%;
		height: 36px;
		padding: 0 10px;
		border: 1px solid var(--pdf-border, #dcdfe6);
		border-radius: 8px;
		background: var(--pdf-input-bg, #fff);
		color: inherit;
		font: inherit;
		outline: none;
		box-sizing: border-box;
	}

	.pdf-modal-input:focus {
		border-color: var(--pdf-accent, #4c6ef5);
	}

	.pdf-modal-error {
		margin: 8px 0 0;
		color: #c92a2a;
		font-size: 12px;
	}

	.pdf-modal-footer {
		display: flex;
		justify-content: flex-end;
		gap: 8px;
		padding: 12px 16px;
		border-top: 1px solid var(--pdf-border, #dcdfe6);
	}

	.pdf-modal-primary,
	.pdf-modal-secondary {
		padding: 7px 16px;
		border-radius: 7px;
		font: inherit;
		font-size: 13px;
		cursor: pointer;
	}

	.pdf-modal-primary {
		border: none;
		background: var(--pdf-accent, #4c6ef5);
		color: #fff;
	}

	.pdf-modal-primary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.pdf-modal-secondary {
		border: 1px solid var(--pdf-border, #dcdfe6);
		background: transparent;
		color: inherit;
	}
</style>
