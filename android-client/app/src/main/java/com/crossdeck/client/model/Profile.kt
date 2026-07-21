package com.crossdeck.client.model

import kotlinx.serialization.Serializable

@Serializable
data class Profile(
    val profileId: String,
    val name: String,
    val buttons: List<ButtonModel>
)

@Serializable
data class ProfileHeader(
    val profileId: String,
    val name: String,
    val icons: List<String> = emptyList()
)

/** A button's position is its index within Profile.buttons (filtered to its parentFolderId) —
 * no explicit coordinates; the deck auto-wraps at a fixed column count. */
@Serializable
data class ButtonModel(
    val buttonId: String,
    val label: String,
    val icon: String? = null,
    val action: ActionModel,
    /** Optional second action fired by long-pressing the button. */
    val longPressAction: ActionModel? = null,
    val parentFolderId: String? = null
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
    val dialTarget: String? = null,
    val mouseX: Int? = null,
    val mouseY: Int? = null,
    val mouseButton: String? = null,
    /** Optional override icon for a long-press action or a multi-action step. */
    val icon: String? = null,
    /** Optional override label for a long-press action or a multi-action step. */
    val label: String? = null
)
