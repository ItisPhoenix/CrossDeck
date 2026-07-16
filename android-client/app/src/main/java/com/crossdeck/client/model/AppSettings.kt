package com.crossdeck.client.model

/** Local-only device preferences (settings drawer) — no protocol/server involvement. */
data class AppSettings(
    val hapticsEnabled: Boolean = true,
    val compactGrid: Boolean = false,
    val keepScreenAwake: Boolean = false,
    val iconOnlyMode: Boolean = false,
    val autoReconnect: Boolean = true,
    val confirmRunCommand: Boolean = false
)
