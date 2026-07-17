package com.crossdeck.client.model

import kotlinx.serialization.Serializable

@Serializable
data class Profile(
    val profileId: String,
    val name: String,
    val buttons: List<ButtonModel>,
    val rows: Int = 3,
    val columns: Int = 5
)

@Serializable
data class ProfileHeader(
    val profileId: String,
    val name: String,
    val icons: List<String> = emptyList()
)

@Serializable
data class ButtonModel(
    val buttonId: String,
    val position: Position,
    val label: String,
    val icon: String? = null,
    val action: ActionModel,
    val parentFolderId: String? = null
)

@Serializable
data class Position(
    val row: Int,
    val col: Int
)

/**
 * Milestone 1 supports "hotkey" and "launch_app" only — see ActionModel.kt equivalent on the
 * Windows Host side for why this is flattened rather than a sealed-class-per-type. Keeping the
 * two sides structurally identical make the protocol easier to reason about while it's still
 * this small; revisit with a polymorphic serializer in Milestone 2.
 */
@Serializable
data class ActionModel(
    val type: String,
    val keys: List<String>? = null,
    val path: String? = null,
    val mediaCommand: String? = null,
    val url: String? = null,
    val command: String? = null,
    val text: String? = null,
    val targetFolderId: String? = null,
    val actions: List<ActionModel>? = null,
    val delays: List<Int>? = null,
    val dialTarget: String? = null
)
