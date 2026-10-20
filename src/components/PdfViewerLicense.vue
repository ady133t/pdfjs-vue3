<script setup lang="ts">
	/**
	 * API-compatibility wrapper for setups written against the proprietary
	 * `PdfViewerLicense` component. This build has no license checks, no key
	 * validation and no network calls — the prop is accepted and ignored.
	 */
	defineProps<{ licenseKey?: string }>();
</script>

<template>
	<slot />
</template>
