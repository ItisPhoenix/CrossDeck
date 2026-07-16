package com.crossdeck.client.model

import kotlinx.serialization.Serializable

/** One installed Windows app, as reported by the host's `list_apps`/`app_list` message. */
@Serializable
data class DiscoveredApp(val name: String, val path: String)
