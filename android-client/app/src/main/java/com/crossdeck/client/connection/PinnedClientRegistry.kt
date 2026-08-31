package com.crossdeck.client.connection

import okhttp3.OkHttpClient

/** Shares the currently pinned client with the deck's icon renderer without exposing trust bypasses. */
internal object PinnedClientRegistry {
    @Volatile
    private var current: OkHttpClient? = null

    fun set(client: OkHttpClient) {
        current = client
    }

    fun get(): OkHttpClient? = current
}
