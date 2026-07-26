package com.crossdeck.client.model

import kotlinx.serialization.Serializable

@Serializable
data class Profile(
    val profileId: String,
    val name: String,
    val buttons: List<ButtonModel>,
    /** Dial strip below the grid — same ButtonModel shape, own list, position = index.
     * action is the dial itself; longPressAction is what a tap on it fires. */
    val dials: List<ButtonModel> = emptyList()
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
    /** Fired by tapping a DIAL specifically (a dial has no separate "main tap action" the way a
     * grid button does — dragging IS the main action, so this is what tap fires). Grid buttons no
     * longer expose this in the editor (one action per button, matching Stream Deck) — see
     * docs/superpowers/specs/2026-07-26-button-action-model-and-editor-redesign-design.md. */
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
    /** Used by multi_action/macro for their steps. Also reused for a dial stack: when
     * type == "dial" and this is set (non-empty), each entry is a full dial layer (its own
     * dialTarget/dialProcess/label) and tapping the dial cycles between them. */
    val actions: List<ActionModel>? = null,
    val delays: List<Int>? = null,
    val dialTarget: String? = null,
    /** Used when dialTarget == "app_volume" to bind the dial to one process's session.
     * Null means "open the live multi-app mixer" (existing behaviour). */
    val dialProcess: String? = null,
    /** Used when dialTarget == "keystroke_step" — each drag detent fires one of these (Up when
     * dragging up, Down when dragging down) instead of setting an absolute 0-100 level. */
    val dialStepUpKeys: List<String>? = null,
    val dialStepDownKeys: List<String>? = null,
    val mouseX: Int? = null,
    val mouseY: Int? = null,
    val mouseButton: String? = null,
    /** Optional override icon for a long-press action or a multi-action step. */
    val icon: String? = null,
    /** Optional override label for a long-press action or a multi-action step. */
    val label: String? = null
)

/** Resolves which dial layer is actually being addressed — actions[stackIndex] for a stacked
 * dial (actions non-empty), or this action itself for a plain unstacked dial. Clamped so an
 * out-of-range index (e.g. a profile edited to fewer layers than this client still remembers)
 * falls back to the last layer instead of crashing. Mirrors the host's ActionModel.ResolveDialLayer. */
fun ActionModel.resolveDialLayer(stackIndex: Int): ActionModel {
    val layers = actions
    return if (!layers.isNullOrEmpty()) layers[stackIndex.coerceIn(0, layers.size - 1)] else this
}
