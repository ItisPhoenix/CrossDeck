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
    /** Optional second action fired by long-pressing the button. */
    val longPressAction: ActionModel? = null,
    val parentFolderId: String? = null
)

@Serializable
data class Position(
    val row: Int,
    val col: Int
)

/**
 * Flattened rather than sealed-class-per-type, mirroring the Windows Host's ActionModel —
 * keeping the two sides structurally identical makes the protocol easier to reason about.
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
