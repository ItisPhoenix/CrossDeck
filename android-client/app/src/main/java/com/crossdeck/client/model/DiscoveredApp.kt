package com.crossdeck.client.model

import kotlinx.serialization.Serializable

/** One installed Windows app, as reported by the host's `list_apps`/`app_list` message. */
@Serializable
data class DiscoveredApp(val name: String, val path: String)

/** One open PC window, as reported by the host's `running_apps` message. */
@Serializable
data class RunningApp(val hwnd: Long, val title: String, val processName: String, val icon: String? = null, val focused: Boolean = false)
