package com.crossdeck.client.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.produceState
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items as lazyColumnItems
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.IconButton
import androidx.compose.ui.graphics.Brush
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.LayoutCoordinates
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.zIndex
import androidx.compose.material3.Icon
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowLeft
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Edit
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.unit.sp
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Snackbar
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.material3.SheetValue
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.horizontalScroll
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.Spring
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextField
import androidx.compose.material3.Slider
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.key
import androidx.compose.ui.window.Popup
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clip
import androidx.compose.material.icons.filled.Close
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.automirrored.filled.List as ListIcon
import androidx.compose.material.icons.filled.Delete
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.animateFloat
import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.snap
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.asImageBitmap
import java.io.File
import okhttp3.OkHttpClient
import okhttp3.Request
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.foundation.layout.size
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalDensity
import android.view.HapticFeedbackConstants
import androidx.compose.ui.platform.LocalView
import com.crossdeck.client.model.ActionModel
import com.crossdeck.client.model.AppSettings
import com.crossdeck.client.ui.theme.Go
import com.crossdeck.client.ui.theme.SignalCyan
import com.crossdeck.client.model.ButtonModel
import com.crossdeck.client.model.Profile
import java.util.UUID
import kotlinx.coroutines.launch
import kotlin.math.roundToInt

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeckGridScreen(
    profile: Profile,
    profiles: List<com.crossdeck.client.model.ProfileHeader>,
    activeProfileId: String,
    connectedHostUrl: String?,
    authToken: String?,
    /** Pair(message, isSuccess) — non-null for ~2.5 s when a toast should show. */
    toastMessage: Pair<String, Boolean>?,
    dialLevels: Map<String, Int>,
    /** buttonId -> live "active" state (Mute actually muted, Play/Pause actually playing, launch_app actually focused). */
    activeButtons: Map<String, Boolean>,
    isPcResponding: Boolean = true,
    accentColorHex: String,
    onAccentColorChange: (String) -> Unit,
    onButtonTap: (ButtonModel) -> Unit,
    onButtonSave: (ButtonModel) -> Unit,
    onButtonsReorder: (parentFolderId: String?, orderedButtonIds: List<String>) -> Unit = { _, _ -> },
    onIconUpload: suspend (ByteArray) -> String?,
    appList: List<com.crossdeck.client.model.DiscoveredApp>,
    onRequestAppList: () -> Unit,
    extractedIcon: Pair<String, String?>?,
    onRequestExtractIcon: (String) -> Unit,
    onButtonDelete: (String) -> Unit,
    onProfileSwitch: (String) -> Unit,
    onProfileCreate: (String) -> Unit,
    onProfileDelete: (String) -> Unit,
    onProfileRename: (String, String) -> Unit,
    onDialAdjust: (buttonId: String, slot: String, value: Int?) -> Unit,
    settings: AppSettings,
    onSettingsChange: (AppSettings) -> Unit,
    connectionHostInfo: String?,
    onForgetHost: () -> Unit,
    onClearIconCache: () -> Unit,
    runningApps: List<com.crossdeck.client.model.RunningApp> = emptyList(),
    onRunningAppsSubscribe: (Boolean) -> Unit = {},
    onWindowFocus: (Long) -> Unit = {},
    onWindowClose: (Long) -> Unit = {},
    onButtonPress: (ButtonModel, String, Int?) -> Unit = { _, _, _ -> },
    audioMixerApps: List<com.crossdeck.client.model.AudioMixerApp> = emptyList(),
    onAudioMixerSubscribe: (Boolean) -> Unit = {},
    onAudioMixerAdjust: (processName: String, value: Int?, muted: Boolean?) -> Unit = { _, _, _ -> },
) {
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val view = LocalView.current
    val density = LocalDensity.current.density

    // Single gate for every haptic call site below — respects the Haptic Feedback setting
    // without wrapping each individual view.performHapticFeedback(...) call in an if-check.
    fun haptic(type: Int = HapticFeedbackConstants.KEYBOARD_TAP) {
        if (settings.hapticsEnabled) view.performHapticFeedback(type)
    }

    // Keep Screen Awake setting — View.keepScreenOn needs no Activity/window reference.
    LaunchedEffect(settings.keepScreenAwake) {
        view.keepScreenOn = settings.keepScreenAwake
    }

    var pendingRunCommand by remember { mutableStateOf<PendingRunCommand?>(null) }

    val accentColor = try {
        Color(android.graphics.Color.parseColor(accentColorHex))
    } catch (e: Exception) {
        SignalCyan
    }

    // Show a Snackbar whenever toastMessage becomes non-null
    LaunchedEffect(toastMessage) {
        toastMessage?.let { (msg, _) ->
            scope.launch { snackbarHostState.showSnackbar(message = msg, withDismissAction = false) }
        }
    }
    var isEditMode by remember { mutableStateOf(false) }
    var editingButton by remember { mutableStateOf<ButtonModel?>(null) }
    var longPressMenuButton by remember { mutableStateOf<ButtonModel?>(null) }
    var pendingRunningAppAssign by remember { mutableStateOf<com.crossdeck.client.model.RunningApp?>(null) }
    var creatingNewButton by remember { mutableStateOf(false) }

    var currentFolderId by remember { mutableStateOf<String?>(null) }
    var folderHistory by remember { mutableStateOf<List<Pair<String, String>>>(emptyList()) }
    var activeDialButton by remember { mutableStateOf<ButtonModel?>(null) }
    var activeDialSlot by remember { mutableStateOf("main") }
    var showAudioMixer by remember { mutableStateOf(false) }

    // Pop one level out of the current folder — used by both the system back button and the
    // on-screen back chevron. folderHistory entries are (parentFolderId, enteredFolderLabel);
    // "" stands in for root since currentFolderId is nullable but the list holds non-null strings.
    fun navigateBackFromFolder() {
        val parent = folderHistory.lastOrNull()?.first
        currentFolderId = parent?.ifEmpty { null }
        folderHistory = folderHistory.dropLast(1)
    }
    BackHandler(enabled = currentFolderId != null) { navigateBackFromFolder() }

    var expanded by remember { mutableStateOf(false) }
    var showCreateDialog by remember { mutableStateOf(false) }
    var newProfileName by remember { mutableStateOf("") }
    var showRenameDialog by remember { mutableStateOf(false) }
    var renameProfileName by remember { mutableStateOf(profile.name) }
    var showSettingsPanel by remember { mutableStateOf(false) }
    var showRunningApps by remember { mutableStateOf(false) }

    // Track profile change state for 3D flip card transition
    var prevActiveProfileId by remember { mutableStateOf(activeProfileId) }
    var triggerFlip by remember { mutableStateOf(false) }
    LaunchedEffect(activeProfileId) {
        if (prevActiveProfileId != activeProfileId) {
            triggerFlip = true
            currentFolderId = null
            folderHistory = emptyList()
            prevActiveProfileId = activeProfileId
            kotlinx.coroutines.delay(600)
            triggerFlip = false
        }
    }

    // 3D rotation animation calculation
    val rotationYAngle by animateFloatAsState(
        targetValue = if (triggerFlip) 180f else 0f,
        animationSpec = tween(durationMillis = 600),
        label = "profileFlip"
    )

    // Auto-flow layout: 5 columns, fixed; rows grow automatically as buttons are added.
    // A button's position is just its index in this filtered list — no more explicit
    // row/col coordinates. Capped at 20 (folders exist for organizing beyond that).
    val columns = 5
    val maxButtons = 20
    val displayedButtons = profile.buttons.filter { it.parentFolderId == currentFolderId }
    val showAddTile = (isEditMode || pendingRunningAppAssign != null) && displayedButtons.size < maxButtons
    val totalCells = displayedButtons.size + if (showAddTile) 1 else 0
    val rows = kotlin.math.ceil(totalCells.coerceAtLeast(1).toFloat() / columns).toInt()

    if (showCreateDialog) {
        AlertDialog(
            onDismissRequest = { showCreateDialog = false },
            modifier = Modifier.border(1.dp, accentColor.copy(alpha = 0.5f), RoundedCornerShape(16.dp)),
            containerColor = MaterialTheme.colorScheme.surface,
            shape = RoundedCornerShape(16.dp),
            title = { Text("Create New Profile", color = MaterialTheme.colorScheme.onSurface) },
            text = {
                CrossDeckTextField(
                    value = newProfileName,
                    onValueChange = { newProfileName = it },
                    label = "Profile Name",
                    modifier = Modifier.fillMaxWidth()
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        if (newProfileName.isNotBlank()) {
                            onProfileCreate(newProfileName.trim())
                            newProfileName = ""
                            showCreateDialog = false
                        }
                    }
                ) {
                    Text("Create", color = accentColor)
                }
            },
            dismissButton = {
                TextButton(onClick = { showCreateDialog = false }) {
                    Text("Cancel", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        )
    }

    if (showRenameDialog) {
        AlertDialog(
            onDismissRequest = { showRenameDialog = false },
            modifier = Modifier.border(1.dp, accentColor.copy(alpha = 0.5f), RoundedCornerShape(16.dp)),
            containerColor = MaterialTheme.colorScheme.surface,
            shape = RoundedCornerShape(16.dp),
            title = { Text("Rename Profile", color = MaterialTheme.colorScheme.onSurface) },
            text = {
                CrossDeckTextField(
                    value = renameProfileName,
                    onValueChange = { renameProfileName = it },
                    label = "New Profile Name",
                    modifier = Modifier.fillMaxWidth()
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        if (renameProfileName.isNotBlank()) {
                            onProfileRename(activeProfileId, renameProfileName.trim())
                            showRenameDialog = false
                        }
                    }
                ) {
                    Text("Rename", color = accentColor)
                }
            },
            dismissButton = {
                TextButton(onClick = { showRenameDialog = false }) {
                    Text("Cancel", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        )
    }

    Scaffold(
        snackbarHost = {
            SnackbarHost(hostState = snackbarHostState) { data ->
                val isSuccess = toastMessage?.second != false
                CrossDeckToast(
                    message = data.visuals.message,
                    dotColor = if (isSuccess) Go else MaterialTheme.colorScheme.error,
                    accentColor = accentColor
                )
            }
        }
    ) { innerPadding ->
        val backgroundColor = MaterialTheme.colorScheme.background
        val bgBrush = remember(accentColor, backgroundColor) {
            Brush.radialGradient(
                colors = listOf(
                    accentColor.copy(alpha = 0.08f),
                    backgroundColor
                ),
                radius = 1200f
            )
        }

        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(bgBrush)
                .padding(innerPadding)
        ) {
            val gridBlurRadius by animateDpAsState(
                targetValue = if (longPressMenuButton != null || showAudioMixer) 18.dp else 0.dp,
                animationSpec = tween(180),
                label = "gridBlurRadius"
            )
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    // Blurs the grid behind the long-press/multi-action popup and the app-volume
                    // mixer sheet so their own content reads clearly against a softened backdrop
                    // instead of competing with a crisp grid of near-identical tiles.
                    .blur(gridBlurRadius)
            ) {
                // Grid layout wrapper with 3D Flip capability
                val canSwipeProfiles = profiles.size > 1 && currentFolderId == null && !isEditMode
                BoxWithConstraints(
                    contentAlignment = Alignment.Center,
                    modifier = Modifier
                        .fillMaxSize()
                        .weight(1f)
                        .padding(8.dp)
                        .then(
                            if (canSwipeProfiles) {
                                // Runs in the Initial pointer pass so it sees (and can claim) a
                                // horizontal drag before DeckButton's own combinedClickable (Main
                                // pass) does — otherwise a swipe starting on top of a button was
                                // unreliable since the button's own tap detector could win the
                                // touch-down first. Only consumes once real horizontal-dominant
                                // motion is seen, so plain taps/vertical gestures pass through.
                                Modifier.pointerInput(profiles, activeProfileId) {
                                    awaitEachGesture {
                                        val down = awaitFirstDown(pass = PointerEventPass.Initial)
                                        var accum = 0f
                                        var horizontalDragClaimed = false
                                        var event: androidx.compose.ui.input.pointer.PointerEvent
                                        do {
                                            event = awaitPointerEvent(pass = PointerEventPass.Initial)
                                            val change = event.changes.firstOrNull { it.id == down.id } ?: break
                                            if (change.pressed) {
                                                val delta = change.positionChange()
                                                accum += delta.x
                                                if (!horizontalDragClaimed && kotlin.math.abs(accum) > 24f && kotlin.math.abs(accum) > kotlin.math.abs(delta.y) * 1.5f) {
                                                    horizontalDragClaimed = true
                                                }
                                                if (horizontalDragClaimed) change.consume()
                                            }
                                        } while (event.changes.any { it.id == down.id && it.pressed })

                                        if (horizontalDragClaimed) {
                                            val idx = profiles.indexOfFirst { it.profileId == activeProfileId }
                                            if (idx >= 0) {
                                                val threshold = 120f
                                                if (accum <= -threshold) {
                                                    haptic()
                                                    onProfileSwitch(profiles[(idx + 1) % profiles.size].profileId)
                                                } else if (accum >= threshold) {
                                                    haptic()
                                                    onProfileSwitch(profiles[(idx - 1 + profiles.size) % profiles.size].profileId)
                                                }
                                            }
                                        }
                                    }
                                }
                            } else Modifier
                        )
                        .graphicsLayer {
                            rotationY = rotationYAngle
                            cameraDistance = 12f * density
                        }
                ) {
                    if (rotationYAngle <= 90f || rotationYAngle >= 270f) {
                        var draggedIndex by remember { mutableStateOf<Int?>(null) }
                        var dragOffset by remember { mutableStateOf(Offset.Zero) }
                        var gridCoordinates by remember { mutableStateOf<LayoutCoordinates?>(null) }

                        val gridSpacing = if (settings.compactGrid) 4.dp else 6.dp
                        // Reserve headroom for the floating menu/back icons so the top row never
                        // renders under them.
                        val topInset = 56.dp
                        // Square cells sized to the tighter axis so the grid fits without scrolling
                        // in the common case; shrinks down to a floor (comfortable tap size), and
                        // only overflows into a scroll if even the floor doesn't fit — with the
                        // 20-button cap this should essentially never trigger in practice.
                        val minCellSize = 60.dp
                        val cellSize = remember(maxWidth, maxHeight, rows, columns, gridSpacing) {
                            val availableW = maxWidth - 4.dp - gridSpacing * (columns - 1)
                            val availableH = maxHeight - 4.dp - topInset - gridSpacing * (rows - 1)
                            val computed = minOf(availableW / columns, availableH / rows).coerceAtLeast(0.dp)
                            if (computed < minCellSize) minCellSize else computed
                        }

                        // Content-sized; the parent Box centers it in both axes. verticalScroll is a
                        // no-op when content already fits (the common case) and only engages once the
                        // floor above is hit and rows still overflow the available height.
                        Column(
                            verticalArrangement = Arrangement.spacedBy(gridSpacing),
                            modifier = Modifier
                                .verticalScroll(rememberScrollState())
                                .onGloballyPositioned { gridCoordinates = it }
                        ) {
                        for (r in 0 until rows) {
                        Row(horizontalArrangement = Arrangement.spacedBy(gridSpacing)) {
                        for (c in 0 until columns) {
                                val index = r * columns + c
                                val cellButton = displayedButtons.getOrNull(index)
                                val isAddTile = index == displayedButtons.size && showAddTile
                                val isDraggingThis = draggedIndex == index

                                val dragModifier = if (isEditMode && cellButton != null) {
                                    // Keyed on displayedButtons, not just index: pointerInput's gesture
                                    // coroutine captures displayedButtons once per key change, and index
                                    // never changes for a cell — after any move/edit the handler
                                    // held a stale map, so onDragEnd saved whatever button *used*
                                    // to be at that cell ("drag copies a random button" bug).
                                    Modifier.pointerInput(index, displayedButtons) {
                                        var hasMoved = false
                                        detectDragGesturesAfterLongPress(
                                            onDragStart = {
                                                hasMoved = false
                                                haptic()
                                                draggedIndex = index
                                                dragOffset = Offset.Zero
                                            },
                                            onDrag = { change, dragAmount ->
                                                change.consume()
                                                hasMoved = true
                                                dragOffset += dragAmount
                                            },
                                            onDragEnd = {
                                                val coords = gridCoordinates
                                                val startIdx = draggedIndex
                                                if (hasMoved && coords != null && startIdx != null && startIdx < displayedButtons.size) {
                                                    val cellWidthPx = coords.size.width / columns
                                                    val cellHeightPx = coords.size.height / rows

                                                    val startR = startIdx / columns
                                                    val startC = startIdx % columns

                                                    val startX = startC * cellWidthPx
                                                    val startY = startR * cellHeightPx

                                                    val touchX = startX + cellWidthPx / 2 + dragOffset.x
                                                    val touchY = startY + cellHeightPx / 2 + dragOffset.y

                                                    val targetC = (touchX / cellWidthPx).toInt().coerceIn(0, columns - 1)
                                                    val targetR = (touchY / cellHeightPx).toInt().coerceIn(0, rows - 1)
                                                    // Reordering, not swapping — a moved button shifts the ones
                                                    // between its old and new spot, like reordering a list.
                                                    val targetIdx = (targetR * columns + targetC).coerceIn(0, displayedButtons.size - 1)

                                                    if (targetIdx != startIdx) {
                                                        haptic()
                                                        val reordered = displayedButtons.map { it.buttonId }.toMutableList()
                                                        val movedId = reordered.removeAt(startIdx)
                                                        reordered.add(targetIdx, movedId)
                                                        onButtonsReorder(currentFolderId, reordered)
                                                    }
                                                }
                                                draggedIndex = null
                                                dragOffset = Offset.Zero
                                            },
                                            onDragCancel = {
                                                draggedIndex = null
                                                dragOffset = Offset.Zero
                                            }
                                        )
                                    }
                                } else Modifier

                                val visualModifier = if (isDraggingThis) {
                                    Modifier
                                        .zIndex(10f)
                                        .graphicsLayer {
                                            translationX = dragOffset.x
                                            translationY = dragOffset.y
                                            scaleX = 1.08f
                                            scaleY = 1.08f
                                            shadowElevation = 8.dp.toPx()
                                        }
                                } else Modifier

                                Box(
                                    modifier = Modifier.size(cellSize)
                                ) {
                                    if (cellButton != null) {
                                        val dialValue = if (cellButton.action.type == "dial") dialLevels["${cellButton.buttonId}:main"] else null
                                        val liveActive = activeButtons[cellButton.buttonId]
                                        DeckButton(
                                            button = cellButton,
                                            isEditMode = isEditMode,
                                            connectedHostUrl = connectedHostUrl,
                                            authToken = authToken,
                                            accentColor = accentColor,
                                            iconOnlyMode = settings.iconOnlyMode,
                                            liveActive = liveActive,
                                            onTap = {
                                                haptic()
                                                val pendingApp = pendingRunningAppAssign
                                                if (pendingApp != null) {
                                                    editingButton = cellButton.copy(
                                                        label = pendingApp.title,
                                                        icon = pendingApp.icon,
                                                        action = ActionModel(type = "launch_app", path = pendingApp.processName)
                                                    )
                                                    pendingRunningAppAssign = null
                                                } else if (isEditMode) {
                                                    editingButton = cellButton
                                                } else if (cellButton.action.type == "multi_action") {
                                                    // Tap opens the menu immediately (no long-press
                                                    // hold delay) — a plain tap has nothing else to
                                                    // do on a chain button, so there's no ambiguity
                                                    // to protect against by waiting.
                                                    longPressMenuButton = cellButton
                                                } else {
                                                    if (cellButton.action.type == "open_folder") {
                                                        val destFolder = cellButton.action.targetFolderId
                                                        if (destFolder != null) {
                                                            folderHistory =
                                                                folderHistory + ((currentFolderId ?: "") to cellButton.label)
                                                            currentFolderId = destFolder
                                                        }
                                                    } else if (cellButton.action.type == "dial" && cellButton.action.dialTarget == "app_volume") {
                                                        showAudioMixer = true
                                                    } else if (cellButton.action.type == "dial") {
                                                        activeDialButton = cellButton
                                                        activeDialSlot = "main"
                                                        onDialAdjust(cellButton.buttonId, "main", null) // fetch current level
                                                    } else if (cellButton.action.type == "run_command" && settings.confirmRunCommand) {
                                                        pendingRunCommand = PendingRunCommand(cellButton, cellButton.action, "short", null)
                                                    } else {
                                                        onButtonTap(cellButton)
                                                    }
                                                }
                                            },
                                            onLongPress = if (!isEditMode && cellButton.action.type != "multi_action" && cellButton.longPressAction != null) {
                                                {
                                                    haptic(HapticFeedbackConstants.LONG_PRESS)
                                                    longPressMenuButton = cellButton
                                                }
                                            } else null,
                                            modifier = visualModifier.then(dragModifier),
                                            levelValue = dialValue
                                        )
                                    } else if (isAddTile) {
                                        // Auto-flow has exactly one "empty" slot — the trailing add
                                        // tile right after the last real button — so there's no more
                                        // positional choice to make; a pending running-app assignment
                                        // (whether or not edit mode is on) or a fresh create both
                                        // always append at the end.
                                        val pendingApp = pendingRunningAppAssign
                                        if (pendingApp != null) {
                                            Box(
                                                modifier = Modifier
                                                    .fillMaxSize()
                                                    .clickable {
                                                        haptic()
                                                        editingButton = ButtonModel(
                                                            buttonId = java.util.UUID.randomUUID().toString(),
                                                            label = pendingApp.title,
                                                            icon = pendingApp.icon,
                                                            action = ActionModel(type = "launch_app", path = pendingApp.processName),
                                                            parentFolderId = currentFolderId
                                                        )
                                                        pendingRunningAppAssign = null
                                                    }
                                                    .background(accentColor.copy(alpha = 0.15f), RoundedCornerShape(18.dp))
                                                    .border(1.2.dp, accentColor.copy(alpha = 0.6f), RoundedCornerShape(18.dp))
                                            )
                                        } else if (isEditMode) {
                                            EmptyEditButton(
                                                onClick = {
                                                    haptic()
                                                    creatingNewButton = true
                                                }
                                            )
                                        }
                                    }
                                    // Otherwise (past the add tile, or outside edit mode with no
                                    // pending assignment): just blank space, nothing rendered.
                                }
                        }
                        }
                        }
                        }
                    }
                }
            }

            // Always-on ambient status — passive, no tap action. The banner below only appears
            // when something's actually wrong; this gives a constant "yes, still fine" signal.
            // Top-left, clear of the settings gear (top-right) and tucked tighter into the corner
            // than the folder-back button (which only shows inside a folder, starting at 16.dp)
            // so the two never overlap.
            Box(
                modifier = Modifier
                    .align(Alignment.TopStart)
                    .padding(top = 8.dp, start = 8.dp)
                    .size(8.dp)
                    .background(
                        if (isPcResponding) Go else MaterialTheme.colorScheme.error,
                        RoundedCornerShape(50)
                    )
            )

            // Persistent status banners live at the top, stacked in one column so simultaneous
            // ones (e.g. PC silent + first-launch hint) never overlap; the transient Snackbar
            // toast owns the bottom — separate regions so nothing competes for the same space.
            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.align(Alignment.TopCenter).padding(top = 8.dp)
            ) {
                if (!isPcResponding) {
                    CrossDeckToast(
                        message = "PC not responding…",
                        dotColor = MaterialTheme.colorScheme.error,
                        accentColor = accentColor
                    )
                }

                pendingRunningAppAssign?.let { pendingApp ->
                    CrossDeckToast(
                        message = "Tap a button to assign \"${pendingApp.title}\"",
                        dotColor = accentColor,
                        accentColor = accentColor,
                        actionLabel = "Cancel",
                        onAction = { pendingRunningAppAssign = null }
                    )
                }

                if (!isEditMode && !settings.hasSeenEmptyCellHint && displayedButtons.size < maxButtons) {
                    CrossDeckToast(
                        message = "Turn on edit mode to add buttons",
                        dotColor = accentColor,
                        accentColor = accentColor,
                        actionLabel = "Got it",
                        onAction = { onSettingsChange(settings.copy(hasSeenEmptyCellHint = true)) }
                    )
                }
            }

            // Folder back button — top-left, mirrors the menu button, only shown inside a folder.
            // Without this a folder was a dead end: nothing else in the UI could get back out.
            if (currentFolderId != null) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier
                        .align(Alignment.TopStart)
                        .padding(16.dp)
                        .clickable {
                            haptic()
                            navigateBackFromFolder()
                        }
                        .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.7f), RoundedCornerShape(50))
                        .border(1.dp, accentColor.copy(alpha = 0.4f), RoundedCornerShape(50))
                        .padding(start = 4.dp, end = 12.dp)
                ) {
                    Icon(
                        imageVector = Icons.AutoMirrored.Filled.KeyboardArrowLeft,
                        contentDescription = "Back",
                        tint = accentColor,
                        modifier = Modifier.size(28.dp)
                    )
                    Text(
                        text = folderHistory.lastOrNull()?.second ?: "Back",
                        color = MaterialTheme.colorScheme.onSurface,
                        style = MaterialTheme.typography.bodyMedium,
                        fontWeight = FontWeight.Medium
                    )
                }
            }

            // Floating menu button — top-right, always visible. Keeps the grid clean (Stream Deck
            // style, no permanent header bar) while still reaching Edit mode, Settings, and
            // profile switching from one place.
            Box(
                modifier = Modifier
                    .align(Alignment.TopEnd)
                    .padding(16.dp)
            ) {
                // Pulsing border while in Edit mode — the old always-visible "Edit/Done" button
                // got folded into this menu, so without a pulse there's no ongoing signal that
                // taps are editing buttons rather than pressing them.
                val editPulse by rememberInfiniteTransition(label = "editPulse")
                    .animateFloat(
                        initialValue = 0.35f,
                        targetValue = 1f,
                        animationSpec = androidx.compose.animation.core.infiniteRepeatable(
                            animation = tween(700, easing = androidx.compose.animation.core.LinearEasing),
                            repeatMode = androidx.compose.animation.core.RepeatMode.Reverse
                        ),
                        label = "editPulseAlpha"
                    )
                IconButton(
                    onClick = { expanded = true },
                    modifier = Modifier
                        .size(36.dp)
                        .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.7f), RoundedCornerShape(50))
                        .border(
                            if (isEditMode) 2.dp else 1.dp,
                            accentColor.copy(alpha = if (isEditMode) editPulse else 0.4f),
                            RoundedCornerShape(50)
                        )
                ) {
                    Icon(
                        imageVector = Icons.Default.Settings,
                        contentDescription = "Menu",
                        tint = accentColor,
                        modifier = Modifier.size(18.dp)
                    )
                }
                DropdownMenu(
                    expanded = expanded,
                    onDismissRequest = { expanded = false }
                ) {
                    DropdownMenuItem(
                        text = { Text(if (isEditMode) "Done Editing" else "Edit Buttons") },
                        leadingIcon = {
                            Icon(
                                imageVector = Icons.Default.Edit,
                                contentDescription = null,
                                tint = if (isEditMode) accentColor else MaterialTheme.colorScheme.onSurface
                            )
                        },
                        onClick = {
                            haptic()
                            isEditMode = !isEditMode
                            expanded = false
                        }
                    )
                    DropdownMenuItem(
                        text = { Text("Settings") },
                        leadingIcon = {
                            Icon(
                                imageVector = Icons.Default.Settings,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.onSurface
                            )
                        },
                        onClick = {
                            haptic()
                            showSettingsPanel = true
                            expanded = false
                        }
                    )
                    DropdownMenuItem(
                        text = { Text("Running Apps") },
                        leadingIcon = {
                            Icon(
                                imageVector = Icons.AutoMirrored.Filled.ListIcon,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.onSurface
                            )
                        },
                        onClick = {
                            haptic()
                            showRunningApps = true
                            expanded = false
                        }
                    )
                    Text(
                        text = "PROFILE: ${profile.name}",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp)
                    )
                    profiles.forEach { p ->
                        val isActive = p.profileId == activeProfileId
                        DropdownMenuItem(
                            text = {
                                Text(
                                    text = p.name,
                                    fontWeight = if (isActive) FontWeight.Bold else FontWeight.Normal,
                                    color = if (isActive) accentColor else MaterialTheme.colorScheme.onSurface
                                )
                            },
                            onClick = {
                                expanded = false
                                onProfileSwitch(p.profileId)
                            }
                        )
                    }
                    DropdownMenuItem(
                        text = { Text("New Profile", color = accentColor) },
                        leadingIcon = {
                            Icon(imageVector = Icons.Default.Add, contentDescription = null, tint = accentColor)
                        },
                        onClick = {
                            expanded = false
                            showCreateDialog = true
                        }
                    )
                    DropdownMenuItem(
                        text = { Text("Rename Current") },
                        leadingIcon = {
                            Icon(imageVector = Icons.Default.Edit, contentDescription = null, tint = MaterialTheme.colorScheme.onSurface)
                        },
                        onClick = {
                            expanded = false
                            renameProfileName = profile.name
                            showRenameDialog = true
                        }
                    )
                    if (profiles.size > 1) {
                        DropdownMenuItem(
                            text = { Text("Delete Current", color = MaterialTheme.colorScheme.error) },
                            leadingIcon = {
                                Icon(imageVector = Icons.Default.Delete, contentDescription = null, tint = MaterialTheme.colorScheme.error)
                            },
                            onClick = {
                                expanded = false
                                onProfileDelete(activeProfileId)
                            }
                        )
                    }
                }
            }

            // Page-dot indicator — one dot per profile, current one accent-filled, tap to switch.
            if (profiles.size > 1) {
                Row(
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(bottom = 8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Spacer(modifier = Modifier.weight(1f))
                    profiles.forEach { p ->
                        val isActive = p.profileId == activeProfileId
                        Box(
                            modifier = Modifier
                                .size(if (isActive) 8.dp else 6.dp)
                                .clickable { onProfileSwitch(p.profileId) }
                                .background(
                                    if (isActive) accentColor else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.25f),
                                    RoundedCornerShape(50)
                                )
                        )
                    }
                    Spacer(modifier = Modifier.weight(1f))
                }
            }

            // Settings drawer — backdrop fades, sheet slides up from the bottom.
            androidx.compose.animation.AnimatedVisibility(
                visible = showSettingsPanel,
                enter = androidx.compose.animation.fadeIn(tween(200)),
                exit = androidx.compose.animation.fadeOut(tween(200))
            ) {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .background(MaterialTheme.colorScheme.background.copy(alpha = 0.5f))
                        .clickable { showSettingsPanel = false },
                    contentAlignment = Alignment.BottomCenter
                ) {
                    androidx.compose.animation.AnimatedVisibility(
                        visible = showSettingsPanel,
                        enter = androidx.compose.animation.slideInVertically(tween(250)) { it },
                        exit = androidx.compose.animation.slideOutVertically(tween(200)) { it }
                    ) {
                        Box(modifier = Modifier.clickable(enabled = false) {}) {
                            SettingsPanel(
                                accentColorHex = accentColorHex,
                                onAccentColorChange = onAccentColorChange,
                                settings = settings,
                                onSettingsChange = onSettingsChange,
                                connectionHostInfo = connectionHostInfo,
                                onForgetHost = onForgetHost,
                                onClearIconCache = onClearIconCache,
                                haptic = { haptic() }
                            )
                        }
                    }
                }
            }

            androidx.compose.animation.AnimatedVisibility(
                visible = showRunningApps,
                enter = androidx.compose.animation.fadeIn(tween(220)) +
                        androidx.compose.animation.scaleIn(tween(220), initialScale = 0.94f),
                exit = androidx.compose.animation.fadeOut(tween(180)) +
                        androidx.compose.animation.scaleOut(tween(180), targetScale = 0.94f)
            ) {
                LaunchedEffect(Unit) { onRunningAppsSubscribe(true) }
                DisposableEffect(Unit) { onDispose { onRunningAppsSubscribe(false) } }
                RunningAppsOverlay(
                    apps = runningApps,
                    connectedHostUrl = connectedHostUrl,
                    authToken = authToken,
                    accentColor = accentColor,
                    onFocus = { hwnd ->
                        haptic()
                        onWindowFocus(hwnd)
                        showRunningApps = false
                    },
                    onClose = { hwnd ->
                        haptic()
                        onWindowClose(hwnd)
                    },
                    onAssign = { app ->
                        haptic()
                        pendingRunningAppAssign = app
                        showRunningApps = false
                    },
                    onDismiss = { showRunningApps = false }
                )
            }

            // Bottom-sheet, same shape as the single-slider dial modal above — not a full-screen
            // picker like Running Apps, since this is still "adjust a level(s)", just several at once.
            if (showAudioMixer) {
                LaunchedEffect(Unit) { onAudioMixerSubscribe(true) }
                DisposableEffect(Unit) { onDispose { onAudioMixerSubscribe(false) } }
                AudioMixerSheet(
                    apps = audioMixerApps,
                    connectedHostUrl = connectedHostUrl,
                    authToken = authToken,
                    accentColor = accentColor,
                    onAdjust = onAudioMixerAdjust,
                    onDismiss = { showAudioMixer = false }
                )
            }

            // Confirm-before-Run-Command safety prompt

            if (pendingRunCommand != null) {
                val pending = pendingRunCommand!!
                AlertDialog(
                    onDismissRequest = { pendingRunCommand = null },
                    modifier = Modifier.border(1.dp, accentColor.copy(alpha = 0.5f), RoundedCornerShape(16.dp)),
                    containerColor = MaterialTheme.colorScheme.surface,
                    shape = RoundedCornerShape(16.dp),
                    title = { Text("Run Command?", color = MaterialTheme.colorScheme.onSurface) },
                    text = { Text("Run '${pending.action.command}'?", color = MaterialTheme.colorScheme.onSurfaceVariant) },
                    confirmButton = {
                        TextButton(onClick = {
                            onButtonPress(pending.button, pending.pressType, pending.stepIndex)
                            pendingRunCommand = null
                        }) {
                            Text("Run", color = MaterialTheme.colorScheme.error)
                        }
                    },
                    dismissButton = {
                        TextButton(onClick = { pendingRunCommand = null }) {
                            Text("Cancel", color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                )
            }

            // Thick fluid bottom-sheet touch-bar slider modal for dials
            if (activeDialButton != null) {
                val button = activeDialButton!!
                val currentLevel = dialLevels["${button.buttonId}:$activeDialSlot"] ?: 50
                var localSliderValue by remember { mutableStateOf(currentLevel.toFloat()) }
                var lastSentValue by remember { mutableStateOf(currentLevel) }
                var isUserDragging by remember { mutableStateOf(false) }

                LaunchedEffect(currentLevel) {
                    if (!isUserDragging) {
                        localSliderValue = currentLevel.toFloat()
                        lastSentValue = currentLevel
                    }
                }

                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .background(MaterialTheme.colorScheme.background.copy(alpha = 0.6f))
                        .clickable { activeDialButton = null }
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .align(Alignment.BottomCenter)
                            .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(topStart = 16.dp, topEnd = 16.dp))
                            .border(1.dp, MaterialTheme.colorScheme.outline, RoundedCornerShape(topStart = 16.dp, topEnd = 16.dp))
                            .padding(24.dp)
                            .clickable(enabled = false) {},
                        horizontalAlignment = Alignment.CenterHorizontally
                    ) {
                        val labelIcon = if (button.action.dialTarget == "brightness") "☀️" else "🔊"
                        val dialTitle = button.action.label?.takeIf { it.isNotBlank() }
                            ?: (if (button.action.dialTarget == "brightness") "Brightness" else "Volume")
                        Text(dialTitle, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
                        Spacer(modifier = Modifier.height(8.dp))
                        Text(
                            text = "$labelIcon ${localSliderValue.toInt()}%",
                            style = MaterialTheme.typography.headlineMedium,
                            fontWeight = androidx.compose.ui.text.font.FontWeight.Bold,
                            color = accentColor
                        )
                        Spacer(modifier = Modifier.height(24.dp))
                        
                        // Thick fluid Touch-Bar Slider styling
                        Slider(
                            value = localSliderValue,
                            onValueChange = { newValue ->
                                isUserDragging = true
                                localSliderValue = newValue
                                val distance = Math.abs(newValue.toInt() - lastSentValue)
                                if (distance >= 5) {
                                    // Trigger subtle haptic detent tick
                                    haptic(HapticFeedbackConstants.CLOCK_TICK)
                                    onDialAdjust(button.buttonId, activeDialSlot, newValue.toInt())
                                    lastSentValue = newValue.toInt()
                                }
                            },
                            onValueChangeFinished = {
                                isUserDragging = false
                                haptic()
                                onDialAdjust(button.buttonId, activeDialSlot, localSliderValue.toInt())
                                lastSentValue = localSliderValue.toInt()
                            },
                            valueRange = 0f..100f,
                            steps = 99,
                            modifier = Modifier.fillMaxWidth().height(48.dp)
                        )
                        Spacer(modifier = Modifier.height(24.dp))
                        Button(
                            onClick = { activeDialButton = null },
                            colors = ButtonDefaults.buttonColors(containerColor = accentColor, contentColor = MaterialTheme.colorScheme.background),
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Text("Dismiss", fontWeight = androidx.compose.ui.text.font.FontWeight.Bold)
                        }
                    }
                }
            }

            // Empty button creation overlay
            if (creatingNewButton) {
                // Keyed on Unit within this if-block's lifetime, not regenerated every recompose —
                // a fresh buttonId each recompose broke every remember(button.buttonId) state below
                // (dropdown, checkbox) since it kept resetting mid-interaction.
                val newButtonPlaceholder = remember(creatingNewButton) {
                    ButtonModel(
                        buttonId = "b_" + UUID.randomUUID().toString().substring(0, 8),
                        label = "",
                        action = ActionModel(type = "hotkey"),
                        parentFolderId = currentFolderId
                    )
                }
                EditButtonDialog(
                    button = newButtonPlaceholder,
                    connectedHostUrl = connectedHostUrl,
                    authToken = authToken,
                    onIconUpload = onIconUpload,
                    appList = appList,
                    onRequestAppList = onRequestAppList,
                    extractedIcon = extractedIcon,
                    onRequestExtractIcon = onRequestExtractIcon,
                    onDismiss = { creatingNewButton = false },
                    onSave = { savedButton ->
                        onButtonSave(savedButton)
                        creatingNewButton = false
                    },
                    onDelete = null,
                )
            }

            // Button Configuration editor popup
            if (editingButton != null) {
                EditButtonDialog(
                    button = editingButton!!,
                    connectedHostUrl = connectedHostUrl,
                    authToken = authToken,
                    onIconUpload = onIconUpload,
                    appList = appList,
                    onRequestAppList = onRequestAppList,
                    extractedIcon = extractedIcon,
                    onRequestExtractIcon = onRequestExtractIcon,
                    onDismiss = { editingButton = null },
                    onSave = { savedButton ->
                        onButtonSave(savedButton)
                        editingButton = null
                    },
                    onDelete = {
                        onButtonDelete(editingButton!!.buttonId)
                        editingButton = null
                    },
                )
            }

            longPressMenuButton?.let { btn ->
                LongPressMenu(
                    button = btn,
                    accentColor = accentColor,
                    connectedHostUrl = connectedHostUrl,
                    authToken = authToken,
                    onFire = { pressType, stepIndex, action ->
                        // Mirrors the main grid's onTap special-casing so a popup tile behaves
                        // exactly as it would if it were the button's own main action.
                        when {
                            action.type == "dial" && action.dialTarget == "app_volume" -> showAudioMixer = true
                            action.type == "dial" -> {
                                val slot = if (pressType == "long") "longPress" else "main"
                                activeDialButton = btn.copy(action = action)
                                activeDialSlot = slot
                                onDialAdjust(btn.buttonId, slot, null)
                            }
                            action.type == "open_folder" -> {
                                val destFolder = action.targetFolderId
                                if (destFolder != null) {
                                    folderHistory = folderHistory + ((currentFolderId ?: "") to btn.label)
                                    currentFolderId = destFolder
                                }
                            }
                            action.type == "run_command" && settings.confirmRunCommand ->
                                pendingRunCommand = PendingRunCommand(btn, action, pressType, stepIndex)
                            else -> onButtonPress(btn, pressType, stepIndex)
                        }
                    },
                    onDismiss = { longPressMenuButton = null }
                )
            }
        }
    }
}

// Shared across all icon fetches instead of constructing a new OkHttpClient per call.
private val iconHttpClient = OkHttpClient()

/**
 * Resolves a ButtonModel.icon value to a displayable bitmap:
 * - "builtin:<name>" -> decoded straight from the bundled assets/builtin/ pack, no network.
 * - "<hash>" -> disk-cached under cacheDir/assets/<hash>.png; fetched (token-authed) from
 *   connectedHostUrl on a cache miss.
 * - null, or a hash with no host connection and no cache hit -> null.
 * Must be called off the main thread (wrap in Dispatchers.IO).
 */
private fun resolveIconBitmap(context: android.content.Context, icon: String?, connectedHostUrl: String?, authToken: String?): ImageBitmap? {
    if (icon == null) return null

    if (icon.startsWith("builtin:")) {
        val name = icon.removePrefix("builtin:")
        return try {
            context.assets.open("builtin/$name.png").use { BitmapFactory.decodeStream(it)?.asImageBitmap() }
        } catch (e: Exception) {
            null
        }
    }

    val assetsDir = File(context.cacheDir, "assets").apply { if (!exists()) mkdirs() }
    val file = File(assetsDir, "$icon.png")
    if (file.exists()) {
        return try {
            BitmapFactory.decodeFile(file.absolutePath)?.asImageBitmap()
        } catch (e: Exception) {
            null
        }
    }

    if (connectedHostUrl == null) return null
    return try {
        val requestBuilder = Request.Builder().url("${connectedHostUrl}assets/$icon")
        authToken?.let { requestBuilder.header("X-CrossDeck-Token", it) }
        iconHttpClient.newCall(requestBuilder.build()).execute().use { response ->
            if (!response.isSuccessful) return@use null
            val bytes = response.body?.bytes() ?: return@use null
            file.writeBytes(bytes)
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
        }
    } catch (e: Exception) {
        null
    }
}

/** Looks up a chrome icon from the bundled builtin pack — same pack button icons use. Returns
 * null if not present so callers keep their current text/emoji fallback. */
@Composable
private fun chromeIcon(name: String): ImageBitmap? {
    val context = LocalContext.current
    var bitmap by remember(name) { mutableStateOf<ImageBitmap?>(null) }
    LaunchedEffect(name) {
        bitmap = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            try {
                context.assets.open("builtin/$name.png").use { BitmapFactory.decodeStream(it)?.asImageBitmap() }
            } catch (e: Exception) {
                null
            }
        }
    }
    return bitmap
}

/** Lazily requests + caches an app's icon on first appearance in a picker row. Fires the same
 * on-demand extract_icon request the main editor uses when a row is selected — here it's fired
 * once per row on first appearance instead, deduped so scrolling doesn't re-request. Renders
 * nothing until the host responds with a hash and iconHashCache picks it up. */
@Composable
private fun AppRowIcon(
    exePath: String,
    iconHash: String?,
    connectedHostUrl: String?,
    authToken: String?,
    onRequestExtractIcon: ((String) -> Unit)?
) {
    val context = LocalContext.current
    var bitmap by remember(exePath, iconHash) { mutableStateOf<ImageBitmap?>(null) }
    var requested by remember(exePath) { mutableStateOf(false) }

    LaunchedEffect(exePath, iconHash) {
        if (iconHash != null) {
            bitmap = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                resolveIconBitmap(context, iconHash, connectedHostUrl, authToken)
            }
        } else if (!requested) {
            requested = true
            onRequestExtractIcon?.invoke(exePath)
        }
    }

    Box(modifier = Modifier.size(18.dp), contentAlignment = Alignment.Center) {
        bitmap?.let { Image(bitmap = it, contentDescription = null, modifier = Modifier.size(18.dp)) }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun DeckButton(
    button: ButtonModel,
    isEditMode: Boolean,
    connectedHostUrl: String?,
    authToken: String?,
    accentColor: Color,
    iconOnlyMode: Boolean = false,
    onTap: () -> Unit,
    onLongPress: (() -> Unit)? = null,
    modifier: Modifier = Modifier,
    levelValue: Int? = null,
    /** Live PC-side state: Mute actually muted, Play/Pause actually playing, launch_app actually
     *  focused. Null = no live state tracked for this button (most action types). */
    liveActive: Boolean? = null
) {
    val context = LocalContext.current
    var imageBitmap by remember(button.icon) { mutableStateOf<ImageBitmap?>(null) }

    LaunchedEffect(button.icon, connectedHostUrl) {
        imageBitmap = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            resolveIconBitmap(context, button.icon, connectedHostUrl, authToken)
        }
    }

    val interactionSource = remember { MutableInteractionSource() }
    // Manual, not collectIsPressedAsState() — that has its own built-in emission delay.
    var isPressed by remember { mutableStateOf(false) }
    // snap() on press-in for zero delay; release still eases so it doesn't look glitchy.
    val animatedScale by animateFloatAsState(
        targetValue = if (isPressed) 0.94f else 1.0f,
        animationSpec = if (isPressed) snap() else tween(durationMillis = 100),
        label = "pressScale"
    )

    val glossyBg = Brush.verticalGradient(
        colors = listOf(
            MaterialTheme.colorScheme.surface.copy(alpha = 0.85f),
            MaterialTheme.colorScheme.background.copy(alpha = 0.85f)
        )
    )

    val glossyBorder = Brush.verticalGradient(
        colors = listOf(
            MaterialTheme.colorScheme.onSurface.copy(alpha = 0.18f),
            MaterialTheme.colorScheme.onSurface.copy(alpha = 0.03f)
        )
    )

    val activeBorder = if (isPressed) {
        Brush.verticalGradient(listOf(accentColor, accentColor.copy(alpha = 0.4f)))
    } else if (isEditMode) {
        Brush.verticalGradient(listOf(accentColor.copy(alpha = 0.6f), accentColor.copy(alpha = 0.1f)))
    } else if (liveActive == true) {
        // Reflects real PC state (actually muted / actually playing / actually focused) rather
        // than just "the tap fired" — closes the fire-and-forget feedback gap.
        Brush.verticalGradient(listOf(accentColor, accentColor.copy(alpha = 0.5f)))
    } else {
        glossyBorder
    }

    // Badge (below) is a sibling of Surface, not nested inside it — Surface clips its content
    // to its shape by default, which was cutting off the badge's deliberate corner overhang.
    BoxWithConstraints(modifier = modifier.fillMaxSize()) {
    val cellSize = maxWidth
    val iconSize = cellSize * if (iconOnlyMode) 0.55f else 0.42f
    val badgeSize = cellSize * 0.27f
    Surface(
        shape = RoundedCornerShape(18.dp),
        color = Color.Transparent,
        modifier = Modifier
            .fillMaxSize()
            .scale(animatedScale)
            .pointerInput(Unit) {
                awaitEachGesture {
                    awaitFirstDown(requireUnconsumed = false)
                    isPressed = true
                    waitForUpOrCancellation()
                    isPressed = false
                }
            }
            .combinedClickable(
                interactionSource = interactionSource,
                indication = null,
                onClick = onTap,
                onLongClick = onLongPress
            )
            .background(glossyBg, RoundedCornerShape(18.dp))
            .border(
                1.2.dp,
                activeBorder,
                RoundedCornerShape(18.dp)
            )
    ) {
        Box(contentAlignment = Alignment.Center, modifier = Modifier.fillMaxSize()) {
            // Multiple Actions always shows a live step mosaic — each segment its own step's real
            // icon (falling back to a type glyph per-step if that step has none) — never a single
            // static button-level icon, so the tile always reflects what the chain actually does.
            val multiActionSteps = if (button.action.type == "multi_action") button.action.actions else null
            if (multiActionSteps != null && multiActionSteps.isNotEmpty()) {
                // Closed-grid preview only — tapping still opens the same full-button popup
                // regardless of which segment is hit (wiring is on the outer Surface's onTap).
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    modifier = Modifier.fillMaxSize().padding(2.dp)
                ) {
                    MosaicStepGrid(
                        steps = multiActionSteps,
                        connectedHostUrl = connectedHostUrl,
                        authToken = authToken,
                        modifier = Modifier.weight(1f).fillMaxWidth()
                    )
                    if (!iconOnlyMode && button.label.isNotBlank()) {
                        Text(
                            text = button.label,
                            textAlign = TextAlign.Center,
                            style = MaterialTheme.typography.bodySmall.copy(fontWeight = FontWeight.Medium),
                            color = MaterialTheme.colorScheme.onSurface,
                            maxLines = 2,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.padding(horizontal = 2.dp, vertical = 2.dp)
                        )
                    }
                }
            } else {
                // Ripple ring that expands + fades on press, giving tactile feedback.
                val rippleScale by animateFloatAsState(
                    targetValue = if (isPressed) 1.6f else 0.6f,
                    animationSpec = if (isPressed) snap() else tween(durationMillis = 100),
                    label = "rippleScale"
                )
                val rippleAlpha by animateFloatAsState(
                    targetValue = if (isPressed) 0.45f else 0f,
                    animationSpec = if (isPressed) snap() else tween(durationMillis = 100),
                    label = "rippleAlpha"
                )
                Box(
                    modifier = Modifier
                        .fillMaxSize(0.7f)
                        .graphicsLayer {
                            scaleX = rippleScale
                            scaleY = rippleScale
                            alpha = rippleAlpha
                        }
                        .border(2.dp, accentColor, RoundedCornerShape(50))
                )
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.Center,
                    modifier = Modifier.padding(6.dp)
                ) {
                    if (imageBitmap != null) {
                        Image(
                            bitmap = imageBitmap!!,
                            contentDescription = button.label,
                            modifier = Modifier.size(iconSize).padding(bottom = 2.dp)
                        )
                    }
                    if (!(iconOnlyMode && imageBitmap != null)) {
                        Text(
                            text = button.label,
                            textAlign = TextAlign.Center,
                            style = MaterialTheme.typography.bodySmall.copy(fontWeight = FontWeight.Medium),
                            color = MaterialTheme.colorScheme.onSurface,
                            maxLines = 2,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.padding(horizontal = 2.dp)
                        )
                    }
                    levelValue?.let {
                        Text(
                            text = "$it%",
                            textAlign = TextAlign.Center,
                            style = MaterialTheme.typography.bodySmall.copy(fontWeight = FontWeight.Bold),
                            color = accentColor,
                            modifier = Modifier.padding(top = 1.dp)
                        )
                    }
                }
            }
        }
    }

    // Small badge showing the long-press action's OWN icon — so between the button's main icon
    // (tap) and this badge (hold), both configured actions are visible at a glance instead of
    // just "something happens on hold". Sibling of Surface (see comment above) so it isn't clipped.
    // Multi-action buttons have no long-press action of their own (the mosaic preview already
    // reads as "this is a chain"), so no badge is shown for them.
    if (button.action.type != "multi_action") {
        button.longPressAction?.let { lp ->
            // A chained long-press (2+ alternatives) shows a small mosaic of every one's own
            // glyph instead of one generic chain-link icon, so the badge actually shows all of
            // them — same overflow-into-"+N" rule the old full-tile multi-action mosaic used.
            val chainSteps = if (lp.type == "multi_action") lp.actions?.takeIf { it.size > 1 } else null
            val lpIcon = if (chainSteps == null) lp.icon ?: defaultBuiltinIconForAction(lp)?.let { "builtin:$it" } else null
            Box(
                contentAlignment = Alignment.Center,
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .offset(x = 2.dp, y = 2.dp)
                    .size(badgeSize)
                    // A chain badge shows several icons at once — accent-tinting the whole badge
                    // behind them competes with each icon's own color, so it stays neutral dark
                    // instead, matching the plain single-glyph badge's accent tint only when
                    // there's just one glyph to tint against.
                    .background(
                        if (chainSteps != null) MaterialTheme.colorScheme.background else accentColor.copy(alpha = 0.92f),
                        RoundedCornerShape(if (chainSteps != null) 30 else 50)
                    )
            ) {
                if (chainSteps != null) {
                    val overflow = (chainSteps.size - 4).coerceAtLeast(0)
                    val cells: List<ActionModel?> = if (overflow > 0) chainSteps.take(3) + null else chainSteps
                    val overflowLabel = "+${overflow + 1}"
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        cells.chunked(2).forEach { row ->
                            Row {
                                row.forEach { step ->
                                    Box(contentAlignment = Alignment.Center, modifier = Modifier.size(badgeSize / 2)) {
                                        if (step == null) {
                                            Text(text = overflowLabel, fontSize = 6.sp, lineHeight = 6.sp)
                                        } else {
                                            val stepIcon = step.icon ?: defaultBuiltinIconForAction(step)?.let { "builtin:$it" }
                                            val bmp by produceState<ImageBitmap?>(initialValue = null, stepIcon, connectedHostUrl) {
                                                value = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                                                    resolveIconBitmap(context, stepIcon, connectedHostUrl, authToken)
                                                }
                                            }
                                            if (bmp != null) {
                                                Image(bitmap = bmp!!, contentDescription = null, modifier = Modifier.fillMaxSize(0.8f))
                                            } else {
                                                Text(text = actionTypeGlyph(step), fontSize = 7.sp, lineHeight = 7.sp)
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                } else if (lpIcon != null) {
                    IconPreview(
                        icon = lpIcon,
                        connectedHostUrl = connectedHostUrl,
                        authToken = authToken,
                        modifier = Modifier.size(badgeSize * 0.6f)
                    )
                } else {
                    Text(
                        text = actionTypeGlyph(lp),
                        fontSize = 11.sp,
                        lineHeight = 11.sp
                    )
                }
            }
        }
    }
    }
}

/** Anchored popup shown on long-press: lists the button's action(s), tap one to fire it,
 * tap outside to cancel with nothing fired. */
@Composable
private fun LongPressMenu(
    button: ButtonModel,
    accentColor: Color,
    connectedHostUrl: String?,
    authToken: String?,
    onFire: (pressType: String, stepIndex: Int?, action: ActionModel) -> Unit,
    onDismiss: () -> Unit
) {
    val mainSteps = button.action.actions?.takeIf { button.action.type == "multi_action" && it.isNotEmpty() }
    val longPressSteps = button.longPressAction?.let { lp -> lp.actions?.takeIf { lp.type == "multi_action" && it.isNotEmpty() } }
    Popup(alignment = Alignment.Center, onDismissRequest = onDismiss) {
        var entered by remember { mutableStateOf(false) }
        LaunchedEffect(Unit) { entered = true }
        val popScale by animateFloatAsState(
            targetValue = if (entered) 1f else 0.7f,
            animationSpec = spring(dampingRatio = Spring.DampingRatioMediumBouncy, stiffness = Spring.StiffnessMedium),
            label = "longPressMenuPopScale"
        )
        val popAlpha by animateFloatAsState(
            targetValue = if (entered) 1f else 0f,
            animationSpec = tween(150),
            label = "longPressMenuPopAlpha"
        )
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            modifier = Modifier.graphicsLayer {
                scaleX = popScale
                scaleY = popScale
                alpha = popAlpha
            }
        ) {
            Text(
                text = if (mainSteps != null || longPressSteps != null) "Tap a button to run it" else "Tap either to run it now",
                style = MaterialTheme.typography.labelSmall,
                color = Color.White.copy(alpha = 0.6f),
                modifier = Modifier.padding(bottom = 8.dp)
            )
            Row(
                horizontalArrangement = Arrangement.spacedBy(14.dp),
                modifier = Modifier.horizontalScroll(rememberScrollState())
            ) {
                if (mainSteps != null) {
                    mainSteps.forEachIndexed { i, step ->
                        DeckButton(
                            button = button.copy(
                                buttonId = "${button.buttonId}_step$i",
                                label = describeActionForMenu(step),
                                icon = step.icon ?: defaultBuiltinIconForAction(step)?.let { "builtin:$it" },
                                action = step,
                                longPressAction = null
                            ),
                            isEditMode = false,
                            connectedHostUrl = connectedHostUrl,
                            authToken = authToken,
                            accentColor = accentColor,
                            onTap = { onFire("short", i, step); onDismiss() },
                            modifier = Modifier.size(96.dp)
                        )
                    }
                } else {
                    DeckButton(
                        button = button.copy(
                            label = describeActionForMenu(button.action),
                            icon = button.icon ?: defaultBuiltinIconForAction(button.action)?.let { "builtin:$it" },
                            longPressAction = null
                        ),
                        isEditMode = false,
                        connectedHostUrl = connectedHostUrl,
                        authToken = authToken,
                        accentColor = accentColor,
                        onTap = { onFire("short", null, button.action); onDismiss() },
                        modifier = Modifier.size(96.dp)
                    )
                    if (longPressSteps != null) {
                        longPressSteps.forEachIndexed { i, step ->
                            DeckButton(
                                button = button.copy(
                                    buttonId = "${button.buttonId}_lpstep$i",
                                    label = describeActionForMenu(step),
                                    icon = step.icon ?: defaultBuiltinIconForAction(step)?.let { "builtin:$it" },
                                    action = step,
                                    longPressAction = null
                                ),
                                isEditMode = false,
                                connectedHostUrl = connectedHostUrl,
                                authToken = authToken,
                                accentColor = accentColor,
                                onTap = { onFire("long", i, step); onDismiss() },
                                modifier = Modifier.size(96.dp)
                            )
                        }
                    } else {
                        button.longPressAction?.let { lp ->
                            DeckButton(
                                button = button.copy(
                                    buttonId = "${button.buttonId}_lp",
                                    label = describeActionForMenu(lp),
                                    icon = lp.icon ?: defaultBuiltinIconForAction(lp)?.let { "builtin:$it" },
                                    action = lp,
                                    longPressAction = null
                                ),
                                isEditMode = false,
                                connectedHostUrl = connectedHostUrl,
                                authToken = authToken,
                                accentColor = accentColor,
                                onTap = { onFire("long", null, lp); onDismiss() },
                                modifier = Modifier.size(96.dp)
                            )
                        }
                    }
                }
            }
        }
    }
}

/** A run_command fire awaiting the confirm-before-run prompt — carries which exact action
 * (main, long-press, or a chain step) and how to fire it once confirmed. */
private data class PendingRunCommand(val button: ButtonModel, val action: ActionModel, val pressType: String, val stepIndex: Int?)

/** Names the specific empty field blocking Save, or null if the action is complete — gates the
 * Save button so a blank hotkey/url/command/etc. can't be saved as a dead button. */
private fun missingFieldHint(state: ActionEditorState): String? = when (state.type) {
    "hotkey" -> if (state.hotkeys.split(",").none { it.isNotBlank() }) "Enter a keyboard shortcut" else null
    "launch_app" -> if (state.path.isBlank() && state.searchQuery.isBlank()) "Choose or type an app" else null
    "open_url" -> if (state.url.isBlank()) "Enter a website URL" else null
    "run_command" -> if (state.command.isBlank()) "Enter a command" else null
    "text_snippet" -> if (state.textValue.isBlank()) "Enter text to paste" else null
    "multi_action" -> if (state.isLongPress) {
        if (state.richSteps.isEmpty()) "Add at least one long-press button" else null
    } else {
        if (state.multiSteps.isEmpty()) "Add at least one step to the chain" else null
    }
    "macro" -> if (state.multiSteps.isEmpty()) "Record at least one step" else null
    else -> null
}

/** Best-guess button label derived from the action being configured, so a fresh button doesn't
 * sit there labeled "" until the user thinks to type one. */
private fun suggestedLabel(state: ActionEditorState): String? = when (state.type) {
    "hotkey" -> state.hotkeys.split(",").map { it.trim() }.filter { it.isNotEmpty() }.joinToString("+").ifBlank { null }
    "launch_app" -> (state.path.ifBlank { state.searchQuery }).trim()
        .substringAfterLast('\\').substringAfterLast('/').substringBeforeLast('.').ifBlank { null }
    "media_control" -> mediaCommandLabels[state.mediaCommand]
    "open_url" -> state.url.trim().removePrefix("https://").removePrefix("http://").substringBefore('/').ifBlank { null }
    "run_command" -> state.command.trim().substringAfterLast('\\').substringAfterLast('/').substringBefore(' ').ifBlank { null }
    "text_snippet" -> state.textValue.trim().take(20).ifBlank { null }
    "dial" -> if (state.dialTarget == "brightness") "Brightness" else "Volume"
    "open_folder" -> "Open Folder"
    "macro" -> "Macro"
    else -> null
}

/** Short one-line summary for a menu row — reuses the same per-type switch as describeStep
 * but for a plain ActionModel (not a StepUiState). */
private fun describeActionForMenu(action: ActionModel): String = action.label?.takeIf { it.isNotBlank() } ?: when (action.type) {
    "hotkey" -> "Keyboard Shortcut: ${action.keys?.joinToString(",") ?: ""}"
    "launch_app" -> "Launch: ${action.path?.substringAfterLast('\\')?.substringAfterLast('/') ?: ""}"
    "media_control" -> "Media: ${action.mediaCommand}"
    "open_url" -> "Open: ${action.url}"
    "run_command" -> "Run: ${action.command ?: ""}"
    "text_snippet" -> "Text: ${action.text?.take(20) ?: ""}"
    "open_folder" -> "Open Folder"
    "multi_action" -> "Multiple Actions (${action.actions?.size ?: 0} steps)"
    "macro" -> "Macro (${action.actions?.size ?: 0} steps)"
    "dial" -> "Dial: ${action.dialTarget}"
    else -> action.type
}

@Composable
private fun EmptyEditButton(onClick: () -> Unit) {
    Surface(
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.background.copy(alpha = 0.4f),
        modifier = Modifier
            .fillMaxSize()
            .border(
                1.dp,
                Brush.verticalGradient(
                    listOf(
                        MaterialTheme.colorScheme.onSurface.copy(alpha = 0.08f),
                        MaterialTheme.colorScheme.onSurface.copy(alpha = 0.02f)
                    )
                ),
                RoundedCornerShape(18.dp)
            )
            .clickable { onClick() }
    ) {
        Box(contentAlignment = Alignment.Center, modifier = Modifier.fillMaxSize()) {
            Icon(
                imageVector = Icons.Default.Add,
                contentDescription = "Add Button",
                tint = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.2f),
                modifier = Modifier.size(22.dp)
            )
        }
    }
}

/** Full-screen live grid of open PC windows. Tap = focus on the PC, long-press = close (confirmed). */
@Composable
private fun RunningAppsOverlay(
    apps: List<com.crossdeck.client.model.RunningApp>,
    connectedHostUrl: String?,
    authToken: String?,
    accentColor: Color,
    onFocus: (Long) -> Unit,
    onClose: (Long) -> Unit,
    onAssign: (com.crossdeck.client.model.RunningApp) -> Unit,
    onDismiss: () -> Unit
) {
    val context = LocalContext.current
    var pendingClose by remember { mutableStateOf<com.crossdeck.client.model.RunningApp?>(null) }
    BackHandler { onDismiss() }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background.copy(alpha = 0.97f))
    ) {
        Text(
            text = "RUNNING APPS",
            style = MaterialTheme.typography.labelSmall,
            color = accentColor,
            modifier = Modifier.align(Alignment.TopStart).padding(20.dp)
        )
        Text(
            text = "✕",
            color = MaterialTheme.colorScheme.onSurface,
            style = MaterialTheme.typography.titleMedium,
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(16.dp)
                .clickable { onDismiss() }
                .padding(8.dp)
        )

        if (apps.isEmpty()) {
            Text(
                text = "Waiting for the PC…",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.align(Alignment.Center)
            )
        } else {
            BoxWithConstraints(
                contentAlignment = Alignment.Center,
                modifier = Modifier.fillMaxSize().padding(top = 56.dp, bottom = 16.dp, start = 16.dp, end = 16.dp)
            ) {
                val n = apps.size
                val spacing = 6.dp
                val cols = kotlin.math.ceil(
                    kotlin.math.sqrt(n * (maxWidth.value / maxHeight.value).toDouble())
                ).toInt().coerceIn(1, n)
                val rows = kotlin.math.ceil(n / cols.toDouble()).toInt()
                val cellSize = minOf(
                    (maxWidth - spacing * (cols - 1)) / cols,
                    (maxHeight - spacing * (rows - 1)) / rows
                ).coerceIn(0.dp, 200.dp) // cap so 2-3 open windows don't become giant tiles

                Column(verticalArrangement = Arrangement.spacedBy(spacing)) {
                    for (r in 0 until rows) {
                        Row(horizontalArrangement = Arrangement.spacedBy(spacing)) {
                            for (c in 0 until cols) {
                                val app = apps.getOrNull(r * cols + c) ?: continue
                                val bmp by produceState<ImageBitmap?>(initialValue = null, app.icon, connectedHostUrl) {
                                    value = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                                        resolveIconBitmap(context, app.icon, connectedHostUrl, authToken)
                                    }
                                }
                                Box(modifier = Modifier.size(cellSize)) {
                                    Column(
                                        horizontalAlignment = Alignment.CenterHorizontally,
                                        verticalArrangement = Arrangement.Center,
                                        modifier = Modifier
                                            .fillMaxSize()
                                            .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.8f), RoundedCornerShape(18.dp))
                                            .border(
                                                if (app.focused) 1.5.dp else 1.dp,
                                                if (app.focused) accentColor else MaterialTheme.colorScheme.outline,
                                                RoundedCornerShape(18.dp)
                                            )
                                            .pointerInput(app.hwnd) {
                                                detectTapGestures(
                                                    onTap = { onFocus(app.hwnd) },
                                                    onLongPress = { pendingClose = app }
                                                )
                                            }
                                            .padding(6.dp)
                                    ) {
                                        bmp?.let {
                                            Image(bitmap = it, contentDescription = null, modifier = Modifier.size(cellSize * 0.4f))
                                            Spacer(modifier = Modifier.height(4.dp))
                                        }
                                        Text(
                                            text = app.title,
                                            textAlign = TextAlign.Center,
                                            style = MaterialTheme.typography.bodySmall,
                                            color = MaterialTheme.colorScheme.onSurface,
                                            maxLines = 2,
                                            overflow = TextOverflow.Ellipsis
                                        )
                                    }
                                    // Assign-to-button affordance — separate from tap (focus) and
                                    // long-press (close), which already own this tile's other gestures.
                                    Box(
                                        modifier = Modifier
                                            .align(Alignment.TopEnd)
                                            .padding(3.dp)
                                            .size(20.dp)
                                            .background(accentColor, RoundedCornerShape(50))
                                            .clickable { onAssign(app) },
                                        contentAlignment = Alignment.Center
                                    ) {
                                        Icon(
                                            Icons.Default.Add,
                                            contentDescription = "Assign to a button",
                                            tint = MaterialTheme.colorScheme.background,
                                            modifier = Modifier.size(14.dp)
                                        )
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    pendingClose?.let { app ->
        AlertDialog(
            onDismissRequest = { pendingClose = null },
            containerColor = MaterialTheme.colorScheme.surface,
            shape = RoundedCornerShape(16.dp),
            title = { Text("Close app?", color = MaterialTheme.colorScheme.onSurface) },
            text = { Text("Close \"${app.title}\" on the PC?", color = MaterialTheme.colorScheme.onSurfaceVariant) },
            confirmButton = {
                TextButton(onClick = {
                    onClose(app.hwnd)
                    pendingClose = null
                }) { Text("Close", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = {
                TextButton(onClick = { pendingClose = null }) {
                    Text("Cancel", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        )
    }
}

/** Live app-volume mixer — one row per app currently playing audio, each with its own 0-100
 * slider and mute toggle. Bottom sheet like the single-dial slider modal, not a full-screen
 * picker like Running Apps — its height follows how many apps are actually playing right now
 * (capped + scrollable past a point, so ten simultaneous apps doesn't run off the top of the screen). */
@Composable
private fun AudioMixerSheet(
    apps: List<com.crossdeck.client.model.AudioMixerApp>,
    connectedHostUrl: String?,
    authToken: String?,
    accentColor: Color,
    onAdjust: (processName: String, value: Int?, muted: Boolean?) -> Unit,
    onDismiss: () -> Unit
) {
    val context = LocalContext.current
    BackHandler { onDismiss() }

    Box(
        modifier = Modifier
            .fillMaxSize()
            // The grid behind is blurred (showAudioMixer drives gridBlurRadius above) instead of
            // dimmed — just a light scrim here so the dismiss-tap area still reads as interactive.
            .background(MaterialTheme.colorScheme.background.copy(alpha = 0.15f))
            .clickable { onDismiss() }
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .align(Alignment.BottomCenter)
                .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(topStart = 16.dp, topEnd = 16.dp))
                .border(1.dp, MaterialTheme.colorScheme.outline, RoundedCornerShape(topStart = 16.dp, topEnd = 16.dp))
                .padding(24.dp)
                .clickable(enabled = false) {},
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text("App Volume", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
            Spacer(modifier = Modifier.height(16.dp))

            if (apps.isEmpty()) {
                Text(
                    text = "Nothing is playing audio right now",
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(vertical = 8.dp)
                )
            } else {
                LazyColumn(
                    // Content-sized up to ~4 rows; a 5th+ app scrolls within the sheet instead of
                    // the sheet itself growing past a comfortable height.
                    modifier = Modifier.fillMaxWidth().heightIn(max = 320.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    lazyColumnItems(apps, key = { it.processName }) { app ->
                        // Local drag state so a slider mid-drag doesn't get overwritten by the next
                        // push-loop tick's server-reported level — same pattern as the single-app dial sheet.
                        var localValue by remember(app.processName) { mutableStateOf(app.level.toFloat()) }
                        var isDragging by remember { mutableStateOf(false) }
                        LaunchedEffect(app.level) { if (!isDragging) localValue = app.level.toFloat() }

                        val bmp by produceState<ImageBitmap?>(initialValue = null, app.icon, connectedHostUrl) {
                            value = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                                resolveIconBitmap(context, app.icon, connectedHostUrl, authToken)
                            }
                        }

                        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                            bmp?.let {
                                Image(bitmap = it, contentDescription = null, modifier = Modifier.size(24.dp))
                                Spacer(modifier = Modifier.width(10.dp))
                            }
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = app.processName,
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurface,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis
                                )
                                Slider(
                                    value = localValue,
                                    onValueChange = { isDragging = true; localValue = it; onAdjust(app.processName, it.toInt(), null) },
                                    onValueChangeFinished = { isDragging = false; onAdjust(app.processName, localValue.toInt(), null) },
                                    valueRange = 0f..100f,
                                    enabled = !app.muted,
                                    modifier = Modifier.fillMaxWidth().height(32.dp)
                                )
                            }
                            Spacer(modifier = Modifier.width(4.dp))
                            IconButton(onClick = { onAdjust(app.processName, null, !app.muted) }) {
                                Text(
                                    text = if (app.muted) "🔇" else "🔊",
                                    style = MaterialTheme.typography.titleMedium,
                                    color = if (app.muted) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.height(16.dp))
            Button(
                onClick = onDismiss,
                colors = ButtonDefaults.buttonColors(containerColor = accentColor, contentColor = MaterialTheme.colorScheme.background),
                modifier = Modifier.fillMaxWidth()
            ) {
                Text("Dismiss", fontWeight = androidx.compose.ui.text.font.FontWeight.Bold)
            }
        }
    }
}

/**
 * Frosted overlay shown when a previously-connected session drops. Meant to be stacked on top of
 * a dimmed, touch-blocked DeckGridScreen showing the last-known profile — see MainActivity's
 * `profile != null && state != Connected` branch.
 */
@Composable
fun ReconnectOverlay(accentColor: Color, onManualConnect: () -> Unit, modifier: Modifier = Modifier) {
    val infiniteTransition = rememberInfiniteTransition(label = "reconnectPulse")
    val pulseAlpha by infiniteTransition.animateFloat(
        initialValue = 0.4f,
        targetValue = 1f,
        animationSpec = androidx.compose.animation.core.infiniteRepeatable(
            animation = tween(900, easing = androidx.compose.animation.core.LinearEasing),
            repeatMode = androidx.compose.animation.core.RepeatMode.Reverse
        ),
        label = "reconnectPulseAlpha"
    )

    Box(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background.copy(alpha = 0.8f))
            // Consumes all touches so the dimmed grid underneath can't be interacted with.
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) {},
        contentAlignment = Alignment.Center
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            androidx.compose.material3.CircularProgressIndicator(
                color = accentColor.copy(alpha = pulseAlpha),
                modifier = Modifier.size(48.dp)
            )
            Spacer(modifier = Modifier.height(16.dp))
            Text("Reconnecting…", color = MaterialTheme.colorScheme.onBackground, style = MaterialTheme.typography.bodyLarge)
            Spacer(modifier = Modifier.height(24.dp))
            OutlinedButton(
                onClick = onManualConnect,
                border = androidx.compose.foundation.BorderStroke(1.dp, accentColor.copy(alpha = 0.5f)),
                shape = RoundedCornerShape(8.dp),
                colors = ButtonDefaults.outlinedButtonColors(contentColor = accentColor)
            ) {
                Text("Manual Connection", color = accentColor)
            }
        }
    }
}

// Raw stored values (protocol/persistence) -> friendly display labels. Dropdowns below store the
// raw value in state but render the friendly label, mirroring EditorWindow.xaml's Content/Tag split.
private val actionTypeLabels = mapOf(
    "hotkey" to "Keyboard Shortcut",
    "launch_app" to "Launch App",
    "media_control" to "Media Control",
    "open_url" to "Open Website",
    "run_command" to "Run Command",
    "text_snippet" to "Text Snippet",
    "open_folder" to "Open Folder",
    "multi_action" to "Multiple Actions",
    "macro" to "Record Macro",
    "dial" to "Dial / Slider"
)
/** Mirrors the host's AutoAssignIcons() exactly, so the client-side preview never disagrees
 * with what the server would fill in anyway (ProfileStore.cs). */
private fun defaultBuiltinIconFor(state: ActionEditorState): String? = when (state.type) {
    "hotkey" -> "keyboard"
    "media_control" -> when (state.mediaCommand) {
        "PlayPause" -> "play"
        "NextTrack" -> "skip-forward"
        "PrevTrack" -> "skip-back"
        "VolumeMute" -> "volume-x"
        else -> "volume-2"
    }
    "launch_app" -> "zap"
    "open_url" -> "globe"
    "run_command" -> "terminal"
    "text_snippet" -> "file-text"
    // Deliberately no default — the grid tile falls back to a live step mosaic (DeckButton's
    // MosaicStepGrid) when there's no icon, same as the host's AutoAssignIcons never filling one
    // in for multi_action either. Auto-filling one here would permanently win over that mosaic.
    "macro" -> "disc"
    "open_folder" -> "folder"
    "dial" -> if (state.dialTarget == "brightness") "sun" else "volume-2"
    else -> null
}

/** Same defaults as defaultBuiltinIconFor, but for a plain ActionModel — used for popup tiles
 * (long-press action, chain steps) which have no icon field of their own to fall back to. */
private fun defaultBuiltinIconForAction(action: ActionModel): String? = when (action.type) {
    "hotkey" -> "keyboard"
    "media_control" -> when (action.mediaCommand) {
        "PlayPause" -> "play"
        "NextTrack" -> "skip-forward"
        "PrevTrack" -> "skip-back"
        "VolumeMute" -> "volume-x"
        else -> "volume-2"
    }
    "launch_app" -> "zap"
    "open_url" -> "globe"
    "run_command" -> "terminal"
    "text_snippet" -> "file-text"
    "multi_action" -> "layers"
    "macro" -> "disc"
    "open_folder" -> "folder"
    "dial" -> if (action.dialTarget == "brightness") "sun" else "volume-2"
    else -> null
}
private val mediaCommandLabels = mapOf(
    "PlayPause" to "Play / Pause",
    "NextTrack" to "Next Track",
    "PrevTrack" to "Previous Track",
    "VolumeUp" to "Volume Up",
    "VolumeDown" to "Volume Down",
    "VolumeMute" to "Mute"
)
private val dialTargetLabels = mapOf(
    "volume" to "Volume",
    "brightness" to "Brightness",
    "app_volume" to "App Volume (live mixer)"
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun EditButtonDialog(
    button: ButtonModel,
    connectedHostUrl: String?,
    authToken: String?,
    onIconUpload: suspend (ByteArray) -> String?,
    appList: List<com.crossdeck.client.model.DiscoveredApp>,
    onRequestAppList: () -> Unit,
    extractedIcon: Pair<String, String?>?,
    onRequestExtractIcon: (String) -> Unit,
    onDismiss: () -> Unit,
    onSave: (ButtonModel) -> Unit,
    onDelete: (() -> Unit)?,
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var iconValue by remember { mutableStateOf(button.icon) }
    // Tracks whether the icon came from the user (upload/built-in pick/extracted app icon) vs.
    // still being the type's auto default — only the latter gets overwritten on a type change.
    var iconUserSet by remember(button.buttonId) { mutableStateOf(button.icon != null) }
    var showBuiltinPicker by remember { mutableStateOf(false) }
    var isUploading by remember { mutableStateOf(false) }
    var showDeleteConfirm by remember { mutableStateOf(false) }

    val imagePickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.GetContent()
    ) { uri: android.net.Uri? ->
        if (uri == null) return@rememberLauncherForActivityResult
        scope.launch {
            isUploading = true
            try {
                val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
                if (bytes != null) {
                    onIconUpload(bytes)?.let { hash -> iconValue = hash; iconUserSet = true }
                }
            } finally {
                isUploading = false
            }
        }
    }

    var label by remember { mutableStateOf(button.label) }
    // Only a brand-new (blank-label) button gets live autofill — an existing custom label is
    // never touched, and typing in the field permanently stops the autofill for this session.
    var labelUserEdited by remember(button.buttonId) { mutableStateOf(button.label.isNotBlank()) }
    val mainActionState = remember(button.buttonId) { ActionEditorState(button.action) }
    var longPressEnabled by remember(button.buttonId) { mutableStateOf(button.longPressAction != null) }
    val longPressState = remember(button.buttonId) { ActionEditorState(button.longPressAction, isLongPress = true) }

    // Only auto-assigns once the action actually has enough to do something (same gate as Save) —
    // picking "Launch App" shouldn't stamp an icon before an app is even chosen.
    LaunchedEffect(
        mainActionState.type, mainActionState.mediaCommand, mainActionState.dialTarget,
        mainActionState.path, mainActionState.searchQuery, mainActionState.url,
        mainActionState.command, mainActionState.textValue, mainActionState.hotkeys
    ) {
        if (!iconUserSet && missingFieldHint(mainActionState) == null) {
            defaultBuiltinIconFor(mainActionState)?.let { iconValue = "builtin:$it" }
        }
    }

    // Autofill the label from the action's own parameters until the user types a custom one.
    LaunchedEffect(
        mainActionState.type, mainActionState.path, mainActionState.searchQuery, mainActionState.mediaCommand,
        mainActionState.url, mainActionState.command, mainActionState.textValue, mainActionState.hotkeys, mainActionState.dialTarget
    ) {
        if (!labelUserEdited) {
            suggestedLabel(mainActionState)?.let { label = it }
        }
    }

    // Auto-icon-on-select (mirrors the PC editor): when the host responds to an extract_icon
    // request for the path currently in the field, and no icon is set yet, use it. Only the main
    // action drives this — long-press has no icon of its own to set.
    LaunchedEffect(extractedIcon) {
        val (extractedPath, extractedHash) = extractedIcon ?: return@LaunchedEffect
        val matchesMainAction = extractedPath == mainActionState.path ||
            (mainActionState.type == "open_url" && extractedPath == mainActionState.url)
        if (matchesMainAction && extractedHash != null && !iconUserSet) {
            iconValue = extractedHash
            iconUserSet = true
        }
    }

    // Accumulates every path->hash the host has responded with, so app-picker rows (which each
    // fire their own extract_icon request on first appearance) can resolve their icon once it
    // arrives, not just the single most-recently-picked one extractedIcon tracks.
    val iconHashCache = remember { mutableStateMapOf<String, String>() }
    LaunchedEffect(extractedIcon) {
        val (extractedPath, extractedHash) = extractedIcon ?: return@LaunchedEffect
        if (extractedHash != null) iconHashCache[extractedPath] = extractedHash
    }

    val accentColor = MaterialTheme.colorScheme.primary

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(
            skipPartiallyExpanded = true,
            confirmValueChange = { it != SheetValue.Hidden } // drag never fully dismisses, only Cancel/X/Save do
        ),
        containerColor = MaterialTheme.colorScheme.background,
        shape = RoundedCornerShape(topStart = 20.dp, topEnd = 20.dp),
        dragHandle = {
            Box(
                modifier = Modifier
                    .padding(vertical = 12.dp)
                    .width(36.dp)
                    .height(4.dp)
                    .background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.15f), RoundedCornerShape(2.dp))
            )
        },
        contentWindowInsets = { WindowInsets(0) }
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .imePadding()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 20.dp, vertical = 8.dp)
        ) {
            val saveHint = missingFieldHint(mainActionState) ?: run {
                if (longPressEnabled && mainActionState.type != "multi_action") {
                    missingFieldHint(longPressState)?.let { "Long-press action: $it" }
                } else null
            }
            val canSave = saveHint == null

            // Header Bar
            Row(
                modifier = Modifier.fillMaxWidth().padding(bottom = 16.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                TextButton(onClick = onDismiss) {
                    Text("Cancel", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Text(
                    text = if (onDelete == null) "Create Button" else "Edit Button",
                    style = MaterialTheme.typography.titleMedium.copy(fontWeight = FontWeight.Bold),
                    color = MaterialTheme.colorScheme.onBackground
                )
                TextButton(
                    enabled = canSave,
                    onClick = {
                        val act = mainActionState.toActionModel()
                        val longPress = if (longPressEnabled && mainActionState.type != "multi_action") longPressState.toActionModel() else null
                        onSave(button.copy(label = label.trim(), icon = iconValue, action = act, longPressAction = longPress))
                    }
                ) {
                    val saveColor = if (canSave) accentColor else MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.4f)
                    chromeIcon("save")?.let { Image(bitmap = it, contentDescription = null, modifier = Modifier.size(16.dp), alpha = if (canSave) 1f else 0.4f) }
                    Spacer(modifier = Modifier.width(4.dp))
                    Text("Save", color = saveColor, fontWeight = FontWeight.Bold)
                }
            }

            saveHint?.let {
                Text(
                    text = it,
                    color = MaterialTheme.colorScheme.error,
                    fontSize = 12.sp,
                    modifier = Modifier.padding(bottom = 12.dp)
                )
            }

            CrossDeckTextField(
                value = label,
                onValueChange = { label = it; labelUserEdited = true },
                label = "Label",
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(modifier = Modifier.height(16.dp))

            // Multiple Actions always shows the live step mosaic instead (a picker between
            // distinct steps), so the button's own icon is dead there. Macro keeps it — it's one
            // atomic action under the hood, so a real icon represents it more honestly than a mosaic.
            if (mainActionState.type != "multi_action") {
            Text("Icon", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 4.dp)) {
                IconPreview(icon = iconValue, connectedHostUrl = connectedHostUrl, authToken = authToken, modifier = Modifier.size(40.dp))
                Spacer(modifier = Modifier.width(8.dp))
                TextButton(onClick = { showBuiltinPicker = true }) { Text("Built-in", color = accentColor) }
                TextButton(onClick = { imagePickerLauncher.launch("image/*") }, enabled = !isUploading) {
                    Text(if (isUploading) "Uploading…" else "Upload", color = accentColor)
                }
                if (iconValue != null) {
                    TextButton(onClick = { iconValue = null; iconUserSet = false }) { Text("Clear", color = MaterialTheme.colorScheme.error) }
                }
            }
            Spacer(modifier = Modifier.height(16.dp))
            }

            key(button.buttonId, "main") {
                ActionTypeEditor(
                    state = mainActionState,
                    appList = appList,
                    onRequestAppList = onRequestAppList,
                    onRequestExtractIcon = onRequestExtractIcon,
                    accentColor = accentColor,
                    connectedHostUrl = connectedHostUrl,
                    authToken = authToken,
                    iconHashCache = iconHashCache,
                    onAppPicked = { name -> if (!labelUserEdited) label = name },
                )
            }

            Spacer(modifier = Modifier.height(20.dp))
            Box(modifier = Modifier.fillMaxWidth().height(1.dp).background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.08f)))
            Spacer(modifier = Modifier.height(16.dp))

            if (mainActionState.type == "multi_action") {
                // A chain doesn't get a separate long-press action — instead, holding the button
                // is how the chain runs at all (a tap does nothing). Two configured sequences on
                // one button would be confusing, so this replaces rather than adds to that slot.
                Text(
                    text = "This chain runs when the button is held, not tapped. No separate long-press action for a Multiple Actions button.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            } else {
                Text("Long-Press Action", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Spacer(modifier = Modifier.height(2.dp))
                Text(
                    text = "Fires when the button is held on the phone. Same options as the main action above.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Spacer(modifier = Modifier.height(8.dp))
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.clickable { longPressEnabled = !longPressEnabled }) {
                    Checkbox(checked = longPressEnabled, onCheckedChange = null)
                    Text("Enable long-press action", color = MaterialTheme.colorScheme.onSurface)
                }
                if (longPressEnabled) {
                    Spacer(modifier = Modifier.height(8.dp))
                    key(button.buttonId, "longpress") {
                        // Button 1 gets the exact same numbered card as button 2+ once chained
                        // (RichStepListEditor's own cards, rendered inside this same
                        // ActionTypeEditor call when longPressState.type == "multi_action") — the
                        // header/add-button below hide once that happens so numbering never shows twice.
                        // The outer long-press slot never shows its own Label/Icon — once chained,
                        // RichStepListEditor's own per-card sub-editors (each showIconPicker=true,
                        // below) are where that belongs, since each is an independent tile in the
                        // long-press popup. The single/unchained state falls back to the
                        // type-derived glyph, same as any chain card that's never had a custom icon set.
                        if (longPressState.type == "multi_action") {
                            ActionTypeEditor(
                                state = longPressState,
                                appList = appList,
                                onRequestAppList = onRequestAppList,
                                onRequestExtractIcon = onRequestExtractIcon,
                                accentColor = accentColor,
                                connectedHostUrl = connectedHostUrl,
                                authToken = authToken,
                                iconHashCache = iconHashCache,
                                showIconPicker = false,
                                onIconUpload = onIconUpload,
                                extractedIcon = extractedIcon,
                            )
                        } else {
                            Surface(
                                shape = RoundedCornerShape(10.dp),
                                color = MaterialTheme.colorScheme.surface.copy(alpha = 0.4f),
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(bottom = 12.dp)
                                    .border(1.dp, MaterialTheme.colorScheme.onSurface.copy(alpha = 0.1f), RoundedCornerShape(10.dp))
                            ) {
                                Column(modifier = Modifier.padding(12.dp)) {
                                    Text(
                                        "Long-Press Button 1",
                                        style = MaterialTheme.typography.labelMedium.copy(fontWeight = FontWeight.Bold),
                                        color = MaterialTheme.colorScheme.onSurfaceVariant
                                    )
                                    Spacer(modifier = Modifier.height(8.dp))
                                    ActionTypeEditor(
                                        state = longPressState,
                                        appList = appList,
                                        onRequestAppList = onRequestAppList,
                                        onRequestExtractIcon = onRequestExtractIcon,
                                        accentColor = accentColor,
                                        connectedHostUrl = connectedHostUrl,
                                        authToken = authToken,
                                        iconHashCache = iconHashCache,
                                        showIconPicker = false,
                                        onIconUpload = onIconUpload,
                                        extractedIcon = extractedIcon,
                                    )
                                }
                            }
                            TextButton(onClick = { longPressState.convertToChain() }, modifier = Modifier.fillMaxWidth()) {
                                Text("+ Add Another Action", color = accentColor)
                            }
                        }
                    }
                }
            }

            if (onDelete != null) {
                Spacer(modifier = Modifier.height(20.dp))
                Button(
                    onClick = { showDeleteConfirm = true },
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error.copy(alpha = 0.15f)),
                    shape = RoundedCornerShape(10.dp),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text("Delete Button", color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.Bold)
                }
            }
            Spacer(modifier = Modifier.height(24.dp))
        }
    }

    if (showBuiltinPicker) {
        BuiltinIconPickerDialog(
            accentColor = accentColor,
            onDismiss = { showBuiltinPicker = false },
            onSelect = { name ->
                iconValue = "builtin:$name"
                iconUserSet = true
                showBuiltinPicker = false
            }
        )
    }

    if (showDeleteConfirm) {
        AlertDialog(
            onDismissRequest = { showDeleteConfirm = false },
            title = { Text("Delete this button?") },
            text = { Text("This can't be undone.") },
            confirmButton = {
                TextButton(onClick = { showDeleteConfirm = false; onDelete?.invoke() }) {
                    Text("Delete", color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.Bold)
                }
            },
            dismissButton = {
                TextButton(onClick = { showDeleteConfirm = false }) { Text("Cancel") }
            }
        )
    }
}

/** Shared feedback-card chrome — same glossy-gradient/accent-border look as every other surface
 * in the app, with a colored status dot instead of a solid success/error/accent fill. Used for
 * the transient Snackbar toast and every persistent status banner, so they read as one system
 * instead of each rolling its own pill shape. */
@Composable
private fun CrossDeckToast(
    message: String,
    dotColor: Color,
    accentColor: Color,
    actionLabel: String? = null,
    onAction: (() -> Unit)? = null
) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .background(
                Brush.verticalGradient(
                    listOf(
                        MaterialTheme.colorScheme.surface.copy(alpha = 0.95f),
                        MaterialTheme.colorScheme.background.copy(alpha = 0.95f)
                    )
                ),
                RoundedCornerShape(14.dp)
            )
            .border(1.2.dp, accentColor.copy(alpha = 0.35f), RoundedCornerShape(14.dp))
            .padding(horizontal = 14.dp, vertical = 10.dp)
    ) {
        Box(modifier = Modifier.size(8.dp).background(dotColor, RoundedCornerShape(50)))
        Spacer(modifier = Modifier.width(10.dp))
        Text(text = message, color = MaterialTheme.colorScheme.onSurface, fontSize = 13.sp)
        if (actionLabel != null && onAction != null) {
            Spacer(modifier = Modifier.width(10.dp))
            Text(
                text = actionLabel,
                color = accentColor,
                fontSize = 13.sp,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.clickable { onAction() }
            )
        }
    }
}

/** Small preview box used in EditButtonDialog — reuses the same resolve/cache logic as DeckButton. */
@Composable
private fun IconPreview(icon: String?, connectedHostUrl: String?, authToken: String?, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    var bitmap by remember(icon) { mutableStateOf<ImageBitmap?>(null) }

    LaunchedEffect(icon, connectedHostUrl) {
        bitmap = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            resolveIconBitmap(context, icon, connectedHostUrl, authToken)
        }
    }

    Box(
        modifier = modifier.background(MaterialTheme.colorScheme.surface, RoundedCornerShape(6.dp)),
        contentAlignment = Alignment.Center
    ) {
        bitmap?.let { Image(bitmap = it, contentDescription = null, modifier = Modifier.size(28.dp)) }
    }
}

/** Grid picker for the bundled built-in icon pack, enumerated straight from Android assets. */
@Composable
private fun BuiltinIconPickerDialog(accentColor: Color, onDismiss: () -> Unit, onSelect: (String) -> Unit) {
    val context = LocalContext.current
    var names by remember { mutableStateOf<List<String>>(emptyList()) }
    var query by remember { mutableStateOf("") }

    LaunchedEffect(Unit) {
        names = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            context.assets.list("builtin")
                ?.filter { it.endsWith(".png") }
                ?.map { it.removeSuffix(".png") }
                ?.sorted()
                ?: emptyList()
        }
    }
    val filteredNames = if (query.isBlank()) names else names.filter { it.contains(query, ignoreCase = true) }

    AlertDialog(
        onDismissRequest = onDismiss,
        modifier = Modifier
            .background(
                Brush.verticalGradient(
                    listOf(
                        MaterialTheme.colorScheme.surface.copy(alpha = 0.95f),
                        MaterialTheme.colorScheme.background.copy(alpha = 0.95f)
                    )
                ),
                RoundedCornerShape(20.dp)
            )
            .border(
                1.dp,
                Brush.verticalGradient(
                    listOf(
                        MaterialTheme.colorScheme.onSurface.copy(alpha = 0.16f),
                        MaterialTheme.colorScheme.onSurface.copy(alpha = 0.02f)
                    )
                ),
                RoundedCornerShape(20.dp)
            ),
        containerColor = Color.Transparent,
        shape = RoundedCornerShape(20.dp),
        title = { Text("Choose Built-in Icon", color = MaterialTheme.colorScheme.onSurface) },
        text = {
            Column {
                CrossDeckTextField(
                    value = query,
                    onValueChange = { query = it },
                    label = "Search icons",
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(modifier = Modifier.height(8.dp))
                LazyVerticalGrid(
                    columns = GridCells.Fixed(5),
                    modifier = Modifier.height(320.dp)
                ) {
                items(filteredNames) { name ->
                    var bitmap by remember(name) { mutableStateOf<ImageBitmap?>(null) }
                    LaunchedEffect(name) {
                        bitmap = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                            try {
                                context.assets.open("builtin/$name.png").use { BitmapFactory.decodeStream(it)?.asImageBitmap() }
                            } catch (e: Exception) {
                                null
                            }
                        }
                    }
                    val swatchInteraction = remember { MutableInteractionSource() }
                    val swatchPressed by swatchInteraction.collectIsPressedAsState()
                    val swatchScale by animateFloatAsState(targetValue = if (swatchPressed) 0.9f else 1f, label = "swatchPressScale")
                    Box(
                        modifier = Modifier
                            .padding(4.dp)
                            .size(48.dp)
                            .scale(swatchScale)
                            .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(6.dp))
                            .border(1.dp, if (swatchPressed) accentColor else MaterialTheme.colorScheme.outline, RoundedCornerShape(6.dp))
                            .clickable(interactionSource = swatchInteraction, indication = null) { onSelect(name) },
                        contentAlignment = Alignment.Center
                    ) {
                        bitmap?.let { Image(bitmap = it, contentDescription = name, modifier = Modifier.size(28.dp)) }
                    }
                }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) { Text("Cancel", color = MaterialTheme.colorScheme.onSurfaceVariant) }
        }
    )
}

/** Mutable per-field state so Compose recomposes on in-place edits (delay, etc.) without replacing the list item. */
class StepUiState(action: ActionModel, delayAfterMs: Int) {
    var type by mutableStateOf(action.type)
    var keys by mutableStateOf(action.keys?.joinToString(",") ?: "")
    var path by mutableStateOf(action.path ?: "")
    var mediaCommand by mutableStateOf(action.mediaCommand ?: "PlayPause")
    var url by mutableStateOf(action.url ?: "")
    var command by mutableStateOf(action.command ?: "")
    var text by mutableStateOf(action.text ?: "")
    var mouseX by mutableStateOf(action.mouseX)
    var mouseY by mutableStateOf(action.mouseY)
    var mouseButton by mutableStateOf(action.mouseButton)
    var delayAfterMs by mutableStateOf(delayAfterMs)
    var icon by mutableStateOf(action.icon)
    var label by mutableStateOf(action.label ?: "")

    fun toActionModel(): ActionModel = when (type) {
        "hotkey" -> ActionModel(type = type, keys = keys.split(",").map { it.trim() }.filter { it.isNotEmpty() }, icon = icon, label = label.trim().ifBlank { null })
        "launch_app" -> ActionModel(type = type, path = path.trim(), icon = icon, label = label.trim().ifBlank { null })
        "media_control" -> ActionModel(type = type, mediaCommand = mediaCommand, icon = icon, label = label.trim().ifBlank { null })
        "open_url" -> ActionModel(type = type, url = url.trim(), icon = icon, label = label.trim().ifBlank { null })
        "run_command" -> ActionModel(type = type, command = command.trim(), icon = icon, label = label.trim().ifBlank { null })
        "text_snippet" -> ActionModel(type = type, text = text, icon = icon, label = label.trim().ifBlank { null })
        "mouse_click" -> ActionModel(type = type, mouseX = mouseX, mouseY = mouseY, mouseButton = mouseButton, icon = icon, label = label.trim().ifBlank { null })
        else -> ActionModel(type = type)
    }
}

/** Multi-action closed-grid preview — one cell per step, each showing that step's own real icon
 * (like a folder's app-icon preview), falling back to a type glyph only for steps with no icon
 * set. Capped at 6 visible segments; beyond that the last one reads "+N". Tapping anywhere still
 * opens the same full popup (wired on the outer Surface, not per-segment). */
@Composable
private fun MosaicStepGrid(steps: List<ActionModel>, connectedHostUrl: String?, authToken: String?, modifier: Modifier = Modifier) {
    val overflow = (steps.size - 6).coerceAtLeast(0)
    // Each cell is either a real step (show its icon/glyph) or, for the trailing "+N" cell, null.
    val cells: List<ActionModel?> = if (overflow > 0) steps.take(5) + null else steps
    val overflowLabel = "+${overflow + 1}"
    val columns = if (cells.size <= 1) 1 else 2
    val context = LocalContext.current
    Column(
        modifier = modifier
            .clip(RoundedCornerShape(14.dp))
            .background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.12f)),
        verticalArrangement = Arrangement.spacedBy(1.dp)
    ) {
        cells.chunked(columns).forEach { row ->
            Row(modifier = Modifier.weight(1f).fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(1.dp)) {
                row.forEach { step ->
                    Box(
                        modifier = Modifier
                            .weight(1f)
                            .fillMaxHeight()
                            .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.9f)),
                        contentAlignment = Alignment.Center
                    ) {
                        if (step == null) {
                            Text(text = overflowLabel, fontSize = 13.sp)
                        } else {
                            val bmp by produceState<ImageBitmap?>(initialValue = null, step.icon, connectedHostUrl) {
                                value = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                                    resolveIconBitmap(context, step.icon, connectedHostUrl, authToken)
                                }
                            }
                            if (bmp != null) {
                                Image(bitmap = bmp!!, contentDescription = null, modifier = Modifier.fillMaxSize(0.75f))
                            } else {
                                Text(text = actionTypeGlyph(step), fontSize = 15.sp)
                            }
                        }
                    }
                }
                repeat(columns - row.size) { Spacer(modifier = Modifier.weight(1f)) }
            }
        }
    }
}

/** Small glyph representing an action's type — used on the long-press corner badge so both a
 * button's actions (tap icon + this) are visible at a glance. */
private fun actionTypeGlyph(action: ActionModel): String = when (action.type) {
    "hotkey" -> "⌨"
    "launch_app" -> "🚀"
    "media_control" -> when (action.mediaCommand) {
        "PlayPause" -> "⏯"
        "NextTrack" -> "⏭"
        "PrevTrack" -> "⏮"
        "VolumeUp" -> "🔊"
        "VolumeDown" -> "🔉"
        "VolumeMute" -> "🔇"
        else -> "🎵"
    }
    "open_url" -> "🌐"
    "run_command" -> "💻"
    "text_snippet" -> "📋"
    "open_folder" -> "📁"
    "multi_action" -> "🔗"
    "macro" -> "⏺"
    "dial" -> "🎚"
    "mouse_click" -> "🖱"
    else -> "•"
}

private fun describeStep(step: StepUiState): String = when (step.type) {
    "hotkey" -> "${actionTypeLabels["hotkey"]}: ${step.keys}"
    "launch_app" -> "${actionTypeLabels["launch_app"]}: ${step.path}"
    "media_control" -> "${actionTypeLabels["media_control"]}: ${step.mediaCommand}"
    "open_url" -> "${actionTypeLabels["open_url"]}: ${step.url}"
    "run_command" -> "${actionTypeLabels["run_command"]}: ${step.command}"
    "text_snippet" -> "${actionTypeLabels["text_snippet"]}: ${step.text}"
    "mouse_click" -> "Mouse Click (${step.mouseButton}) at ${step.mouseX},${step.mouseY}"
    else -> step.type
}

/**
 * Full action-type + parameter state for one top-level action (main tap or long-press) — mirrors
 * the Windows host's ActionConfigControl, giving long-press the same dropdown + rich per-type
 * fields as the main action instead of a bare type+value row.
 */
class ActionEditorState(action: ActionModel?, val isLongPress: Boolean = false) {
    var type by mutableStateOf(action?.type ?: "hotkey")
    var hotkeys by mutableStateOf(action?.keys?.joinToString(",") ?: "")
    var path by mutableStateOf(action?.path ?: "")
    var mediaCommand by mutableStateOf(action?.mediaCommand ?: "PlayPause")
    var url by mutableStateOf(action?.url ?: "")
    var command by mutableStateOf(action?.command ?: "")
    var textValue by mutableStateOf(action?.text ?: "")
    var targetFolderId by mutableStateOf(action?.targetFolderId ?: "")
    var dialTarget by mutableStateOf(action?.dialTarget ?: "volume")
    var searchQuery by mutableStateOf("")
    var icon by mutableStateOf(action?.icon)
    var label by mutableStateOf(action?.label ?: "")
    // Only a brand-new (blank-label) instance gets live autofill from the action's own
    // parameters — an existing custom label is never touched once the user's typed into it.
    var labelUserEdited by mutableStateOf(!action?.label.isNullOrBlank())
    val multiSteps = mutableStateListOf<StepUiState>()
    // Long-press's own multi_action chain is a picker between independent full sub-buttons, not
    // a sequential chain of light steps — kept separate from multiSteps (still used by Main's
    // compact Multiple-Actions/Macro editor) so the two never collide.
    val richSteps = mutableStateListOf<ActionEditorState>()

    init {
        when {
            // mouse_click only exists as a recorded step inside a chain — there's no "Mouse
            // Click" entry in the action-type dropdown, so a bare mouse_click action (the shape
            // a single recorded click used to be saved as) would match nothing and silently
            // revert to a blank hotkey on save. Wrap it as a one-step chain instead.
            action?.type == "mouse_click" -> {
                type = "multi_action"
                if (isLongPress) richSteps.add(ActionEditorState(action)) else multiSteps.add(StepUiState(action, 0))
            }
            action?.type == "macro" -> {
                action.actions?.forEachIndexed { i, act -> multiSteps.add(StepUiState(act, action.delays?.getOrNull(i) ?: 0)) }
            }
            action?.type == "multi_action" -> {
                if (isLongPress) {
                    action.actions?.forEach { act -> richSteps.add(ActionEditorState(act)) }
                } else {
                    action.actions?.forEachIndexed { i, act -> multiSteps.add(StepUiState(act, action.delays?.getOrNull(i) ?: 0)) }
                }
            }
        }
    }

    fun toActionModel(): ActionModel = when (type) {
        "hotkey" -> ActionModel(type = type, keys = hotkeys.split(",").map { it.trim() }.filter { it.isNotEmpty() }, icon = icon, label = label.trim().ifBlank { null })
        "launch_app" -> ActionModel(type = type, path = (path.ifBlank { searchQuery }).trim(), icon = icon, label = label.trim().ifBlank { null })
        "media_control" -> ActionModel(type = type, mediaCommand = mediaCommand, icon = icon, label = label.trim().ifBlank { null })
        "open_url" -> ActionModel(type = type, url = url.trim(), icon = icon, label = label.trim().ifBlank { null })
        "run_command" -> ActionModel(type = type, command = command.trim(), icon = icon, label = label.trim().ifBlank { null })
        "text_snippet" -> ActionModel(type = type, text = textValue, icon = icon, label = label.trim().ifBlank { null })
        "open_folder" -> ActionModel(
            type = type,
            targetFolderId = targetFolderId.trim().ifBlank { "f_" + UUID.randomUUID().toString().substring(0, 8) },
            icon = icon,
            label = label.trim().ifBlank { null }
        )
        "multi_action" -> if (isLongPress)
            ActionModel(type = type, actions = richSteps.map { it.toActionModel() }, label = label.trim().ifBlank { null })
        else
            ActionModel(type = type, actions = multiSteps.map { it.toActionModel() }, delays = multiSteps.map { it.delayAfterMs }, label = label.trim().ifBlank { null })
        "macro" -> ActionModel(type = type, actions = multiSteps.map { it.toActionModel() }, delays = multiSteps.map { it.delayAfterMs }, label = label.trim().ifBlank { null })
        "dial" -> ActionModel(type = type, dialTarget = dialTarget, icon = icon, label = label.trim().ifBlank { null })
        else -> ActionModel(type = type)
    }
}

/** Converts the current single-action config into step 0 of a chain, for the long-press
 * editor's "+ Add Another Action" affordance. */
private fun ActionEditorState.convertToChain() {
    if (isLongPress) {
        val currentAsSubButton = ActionEditorState(toActionModel())
        richSteps.clear()
        richSteps.add(currentAsSubButton)
    } else {
        val currentAsStep = StepUiState(toActionModel(), 0)
        multiSteps.clear()
        multiSteps.add(currentAsStep)
    }
    type = "multi_action"
}

/** Action-type dropdown + per-type parameter card — reused for both the main action and the
 * long-press action so long-press gets the exact same picker set as the main action. */
@Composable
private fun ActionTypeEditor(
    state: ActionEditorState,
    appList: List<com.crossdeck.client.model.DiscoveredApp>,
    onRequestAppList: () -> Unit,
    onRequestExtractIcon: ((String) -> Unit)?,
    accentColor: Color,
    connectedHostUrl: String?,
    authToken: String?,
    iconHashCache: Map<String, String>,
    onAppPicked: ((name: String) -> Unit)? = null,
    showIconPicker: Boolean = false,
    onIconUpload: (suspend (ByteArray) -> String?)? = null,
    allowChaining: Boolean = true,
    extractedIcon: Pair<String, String?>? = null,
) {
    var dropdownExpanded by remember { mutableStateOf(false) }
    var mediaDropdownExpanded by remember { mutableStateOf(false) }
    var dialDropdownExpanded by remember { mutableStateOf(false) }
    var pathDropdownExpanded by remember { mutableStateOf(false) }
    var showBuiltinIconPicker by remember { mutableStateOf(false) }
    var isUploadingIcon by remember { mutableStateOf(false) }
    val iconScope = rememberCoroutineScope()
    val iconContext = LocalContext.current
    val iconUploadLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.GetContent()
    ) { uri: android.net.Uri? ->
        if (uri == null) return@rememberLauncherForActivityResult
        iconScope.launch {
            isUploadingIcon = true
            try {
                val bytes = iconContext.contentResolver.openInputStream(uri)?.use { it.readBytes() }
                if (bytes != null) onIconUpload?.invoke(bytes)?.let { hash -> state.icon = hash }
            } finally {
                isUploadingIcon = false
            }
        }
    }
    // Not re-keyed on state.searchQuery — that reset the cursor to the end on every keystroke.
    var searchFieldValue by remember {
        mutableStateOf(TextFieldValue(text = state.searchQuery, selection = TextRange(state.searchQuery.length)))
    }
    var hasUserEditedSearchQuery by remember { mutableStateOf(false) }

    LaunchedEffect(state.path, appList) {
        // Once the user has typed/picked, never let the async app-list correction overwrite it.
        if (!hasUserEditedSearchQuery) {
            val matched = appList.find { it.path == state.path }
            val resolved = matched?.name ?: state.path
            state.searchQuery = resolved
            searchFieldValue = TextFieldValue(text = resolved, selection = TextRange(resolved.length))
        }
    }
    LaunchedEffect(state.type) {
        if (state.type == "launch_app") onRequestAppList()
    }
    // Icon auto-extraction is opt-in — every instance now wires a live extract_icon request
    // (main action's, plus long-press/chain cards since they're independent popup tiles that
    // deserve real favicons/app icons too, not just a type-derived glyph).
    if (onRequestExtractIcon != null) {
        LaunchedEffect(state.url, state.type) {
            if (state.type == "open_url" && state.url.isNotBlank()) {
                kotlinx.coroutines.delay(700)
                onRequestExtractIcon(state.url)
            }
        }
        LaunchedEffect(state.path, state.type) {
            if (state.type == "launch_app" && state.path.isNotBlank()) {
                kotlinx.coroutines.delay(400)
                onRequestExtractIcon(state.path)
            }
        }
    }
    // Applies to this instance's OWN state.icon — every instance except the plain main action
    // owns its own Action.icon (written even while it's not shown in the UI, e.g. the long-press
    // top-level slot, since it still drives that popup tile's icon); the main action's icon lives
    // on the button itself (EditButtonDialog's own iconValue + its own matching effect), not here.
    if (state.isLongPress || !allowChaining) {
        LaunchedEffect(extractedIcon) {
            val (extractedPath, extractedHash) = extractedIcon ?: return@LaunchedEffect
            val matches = extractedPath == state.path || (state.type == "open_url" && extractedPath == state.url)
            if (matches && extractedHash != null && state.icon == null) {
                state.icon = extractedHash
            }
        }
    }
    // Label autofill only matters where the Label field is actually visible (showIconPicker) —
    // silently populating a hidden field would contradict why it's hidden at the long-press
    // top-level in the first place (see the "removed the top label/icon" decision above).
    if (showIconPicker) {
        LaunchedEffect(
            state.type, state.path, state.mediaCommand, state.url, state.command, state.textValue, state.hotkeys, state.dialTarget
        ) {
            if (!state.labelUserEdited) {
                suggestedLabel(state)?.let { state.label = it }
            }
        }
    }

    InlineDropdownField(
        label = "Action Type",
        selectedLabel = actionTypeLabels[state.type] ?: state.type,
        expanded = dropdownExpanded,
        onExpandedChange = { dropdownExpanded = it },
        modifier = Modifier.fillMaxWidth()
    ) {
        // Long-press (top-level slot or a chain's own sub-button card) never offers Multiple
        // Actions/Open Folder directly — a long-press chain is only ever built via
        // convertToChain()'s "+ Add Another Action" (never nested inside itself either, no
        // chain-of-chains), and folder navigation is client-side-only paging that doesn't make
        // sense as something you hold a button to run. Macro stays available at the top-level
        // long-press slot — only chain sub-buttons exclude it, same no-nesting rule as Multiple Actions.
        val availableTypes = when {
            !allowChaining -> actionTypeLabels.filterKeys { it != "multi_action" && it != "macro" && it != "open_folder" }
            state.isLongPress -> actionTypeLabels.filterKeys { it != "multi_action" && it != "open_folder" }
            else -> actionTypeLabels
        }
        availableTypes.forEach { (t, friendly) ->
            Text(
                text = friendly,
                color = MaterialTheme.colorScheme.onSurface,
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable { state.type = t; dropdownExpanded = false }
                    .padding(horizontal = 16.dp, vertical = 12.dp)
            )
        }
    }
    Spacer(modifier = Modifier.height(16.dp))

    if (showIconPicker) {
        CrossDeckTextField(
            value = state.label,
            onValueChange = { state.label = it; state.labelUserEdited = true },
            label = "Label (optional, shown on this step's tile)",
            modifier = Modifier.fillMaxWidth().padding(bottom = 16.dp)
        )
        // Multi-action/macro have no icon of their own — each step already shows its own icon.
        if (state.type != "multi_action" && state.type != "macro") {
        Text("Icon", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 4.dp, bottom = 16.dp)) {
            IconPreview(icon = state.icon, connectedHostUrl = connectedHostUrl, authToken = authToken, modifier = Modifier.size(36.dp))
            Spacer(modifier = Modifier.width(8.dp))
            TextButton(onClick = { showBuiltinIconPicker = true }) { Text("Built-in", color = accentColor) }
            TextButton(onClick = { iconUploadLauncher.launch("image/*") }, enabled = !isUploadingIcon) {
                Text(if (isUploadingIcon) "Uploading…" else "Upload", color = accentColor)
            }
            if (state.icon != null) {
                TextButton(onClick = { state.icon = null }) { Text("Clear", color = MaterialTheme.colorScheme.error) }
            }
        }
        if (showBuiltinIconPicker) {
            BuiltinIconPickerDialog(
                accentColor = accentColor,
                onDismiss = { showBuiltinIconPicker = false },
                onSelect = { name -> state.icon = "builtin:$name"; showBuiltinIconPicker = false }
            )
        }
        }
    }

    Surface(
        shape = RoundedCornerShape(12.dp),
        color = MaterialTheme.colorScheme.surface.copy(alpha = 0.5f),
        modifier = Modifier
            .fillMaxWidth()
            .border(
                1.dp,
                Brush.verticalGradient(
                    listOf(
                        MaterialTheme.colorScheme.onSurface.copy(alpha = 0.08f),
                        MaterialTheme.colorScheme.onSurface.copy(alpha = 0.02f)
                    )
                ),
                RoundedCornerShape(12.dp)
            )
            .padding(14.dp)
    ) {
        Column {
            Text(
                text = "Action Parameters",
                style = MaterialTheme.typography.labelSmall.copy(fontWeight = FontWeight.Bold),
                color = accentColor,
                modifier = Modifier.padding(bottom = 8.dp)
            )
            when (state.type) {
                "hotkey" -> {
                    CrossDeckTextField(
                        value = state.hotkeys,
                        onValueChange = { state.hotkeys = it },
                        label = "Keys (comma-separated, e.g. Ctrl,Alt,A)",
                        modifier = Modifier.fillMaxWidth()
                    )
                }
                "launch_app" -> {
                    val filteredApps = remember(state.searchQuery, appList) {
                        if (state.searchQuery.isBlank()) appList
                        else appList.filter {
                            it.name.contains(state.searchQuery, ignoreCase = true) ||
                            it.path.contains(state.searchQuery, ignoreCase = true)
                        }
                    }
                    OutlinedTextField(
                        value = searchFieldValue,
                        onValueChange = { newValue ->
                            hasUserEditedSearchQuery = true
                            searchFieldValue = newValue
                            state.searchQuery = newValue.text
                            pathDropdownExpanded = true
                        },
                        label = { Text("Application Path (pick or type custom)") },
                        colors = androidx.compose.material3.OutlinedTextFieldDefaults.colors(
                            focusedTextColor = MaterialTheme.colorScheme.onSurface,
                            unfocusedTextColor = MaterialTheme.colorScheme.onSurface,
                            focusedBorderColor = MaterialTheme.colorScheme.primary,
                            unfocusedBorderColor = MaterialTheme.colorScheme.outline,
                            focusedLabelColor = MaterialTheme.colorScheme.primary,
                            unfocusedLabelColor = MaterialTheme.colorScheme.onSurfaceVariant,
                            focusedContainerColor = MaterialTheme.colorScheme.surface,
                            unfocusedContainerColor = MaterialTheme.colorScheme.surface
                        ),
                        shape = RoundedCornerShape(10.dp),
                        modifier = Modifier.fillMaxWidth()
                    )
                    if (pathDropdownExpanded && filteredApps.isNotEmpty()) {
                        Surface(
                            shape = RoundedCornerShape(10.dp),
                            color = MaterialTheme.colorScheme.surfaceVariant,
                            modifier = Modifier.fillMaxWidth().padding(top = 4.dp)
                        ) {
                            // LazyColumn, not Column+forEach: only visible rows compose and fire their
                            // icon request, instead of every installed app requesting at once on open.
                            LazyColumn(modifier = Modifier.heightIn(max = 280.dp)) {
                                lazyColumnItems(filteredApps, key = { it.path }) { app ->
                                    Row(
                                        verticalAlignment = Alignment.CenterVertically,
                                        modifier = Modifier
                                            .fillMaxWidth()
                                            .clickable {
                                                hasUserEditedSearchQuery = true
                                                state.path = app.path
                                                state.searchQuery = app.name
                                                searchFieldValue = TextFieldValue(text = app.name, selection = TextRange(app.name.length))
                                                pathDropdownExpanded = false
                                                if (onRequestExtractIcon != null) onRequestExtractIcon(app.path)
                                                onAppPicked?.invoke(app.name)
                                            }
                                            .padding(horizontal = 16.dp, vertical = 10.dp)
                                    ) {
                                        AppRowIcon(app.path, iconHashCache[app.path], connectedHostUrl, authToken, onRequestExtractIcon)
                                        Spacer(modifier = Modifier.width(10.dp))
                                        Text(text = app.name, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                                    }
                                }
                            }
                        }
                    }
                    if (state.path.isNotEmpty() && state.path != state.searchQuery) {
                        Spacer(modifier = Modifier.height(4.dp))
                        Text(
                            text = "Target: ${state.path}",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.padding(horizontal = 4.dp)
                        )
                    }
                }
                "media_control" -> {
                    InlineDropdownField(
                        label = "Media Command",
                        selectedLabel = mediaCommandLabels[state.mediaCommand] ?: state.mediaCommand,
                        expanded = mediaDropdownExpanded,
                        onExpandedChange = { mediaDropdownExpanded = it },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        mediaCommandLabels.forEach { (cmd, friendly) ->
                            Text(
                                text = friendly,
                                color = MaterialTheme.colorScheme.onSurface,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .clickable { state.mediaCommand = cmd; mediaDropdownExpanded = false }
                                    .padding(horizontal = 16.dp, vertical = 12.dp)
                            )
                        }
                    }
                }
                "open_url" -> {
                    CrossDeckTextField(
                        value = state.url,
                        onValueChange = { state.url = it },
                        label = "URL",
                        modifier = Modifier.fillMaxWidth()
                    )
                }
                "run_command" -> {
                    CrossDeckTextField(
                        value = state.command,
                        onValueChange = { state.command = it },
                        label = "Command",
                        modifier = Modifier.fillMaxWidth()
                    )
                }
                "text_snippet" -> {
                    CrossDeckTextField(
                        value = state.textValue,
                        onValueChange = { state.textValue = it },
                        label = "Text Snippet",
                        modifier = Modifier.fillMaxWidth()
                    )
                }
                "open_folder" -> {
                    CrossDeckTextField(
                        value = state.targetFolderId,
                        onValueChange = { state.targetFolderId = it },
                        label = "Target Folder ID",
                        modifier = Modifier.fillMaxWidth()
                    )
                }
                "multi_action" -> {
                    if (state.isLongPress) {
                        RichStepListEditor(
                            steps = state.richSteps,
                            appList = appList,
                            onRequestAppList = onRequestAppList,
                            onRequestExtractIcon = onRequestExtractIcon,
                            accentColor = accentColor,
                            connectedHostUrl = connectedHostUrl,
                            authToken = authToken,
                            iconHashCache = iconHashCache,
                            onIconUpload = onIconUpload,
                            extractedIcon = extractedIcon,
                        )
                    } else {
                        ActionStepListEditor(steps = state.multiSteps)
                    }
                }
                "macro" -> {
                    // Macro is captured by recording real input, not by hand-picking a type and
                    // typing a value — that manual row belongs to Multiple Actions, not here.
                    ActionStepListEditor(steps = state.multiSteps, allowManualAdd = false)
                }
                "dial" -> {
                    InlineDropdownField(
                        label = "Dial Target",
                        selectedLabel = dialTargetLabels[state.dialTarget] ?: state.dialTarget,
                        expanded = dialDropdownExpanded,
                        onExpandedChange = { dialDropdownExpanded = it },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        dialTargetLabels.forEach { (target, friendly) ->
                            Text(
                                text = friendly,
                                color = MaterialTheme.colorScheme.onSurface,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .clickable { state.dialTarget = target; dialDropdownExpanded = false }
                                    .padding(horizontal = 16.dp, vertical = 12.dp)
                            )
                        }
                    }
                    if (state.dialTarget == "app_volume") {
                        Spacer(modifier = Modifier.height(12.dp))
                        Text(
                            "Opens a live mixer on the phone with a slider + mute for every app currently playing audio — no app to pick here.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun ActionStepListEditor(steps: MutableList<StepUiState>, modifier: Modifier = Modifier, allowManualAdd: Boolean = true) {
    var newType by remember { mutableStateOf("hotkey") }
    var newValue by remember { mutableStateOf("") }
    var typeDropdownExpanded by remember { mutableStateOf(false) }
    var iconPickerForIndex by remember { mutableStateOf<Int?>(null) }
    val accentColor = MaterialTheme.colorScheme.primary
    // open_folder is client-side-only navigation — a no-op inside a PC-side chain, so it's not
    // offered here (matches Windows' ActionStepListControl, which never included it).
    val addableTypes = listOf("hotkey", "media_control", "launch_app", "open_url", "run_command", "text_snippet")

    Column(modifier = modifier) {
        steps.forEachIndexed { index, step ->
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 4.dp)
                    .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.5f), RoundedCornerShape(8.dp))
                    .padding(8.dp)
            ) {
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                    IconPreview(
                        icon = step.icon,
                        connectedHostUrl = null,
                        authToken = null,
                        modifier = Modifier.size(28.dp).clickable { iconPickerForIndex = index }
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = describeStep(step),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.weight(1f)
                    )
                    IconButton(onClick = { if (index > 0) { val s = steps.removeAt(index); steps.add(index - 1, s) } }, enabled = index > 0) {
                        Icon(Icons.Default.KeyboardArrowUp, contentDescription = "Move up")
                    }
                    IconButton(onClick = { if (index < steps.size - 1) { val s = steps.removeAt(index); steps.add(index + 1, s) } }, enabled = index < steps.size - 1) {
                        Icon(Icons.Default.KeyboardArrowDown, contentDescription = "Move down")
                    }
                    IconButton(onClick = { steps.removeAt(index) }) {
                        Icon(Icons.Default.Close, contentDescription = "Remove step", tint = MaterialTheme.colorScheme.error)
                    }
                }
                CrossDeckTextField(
                    value = step.label,
                    onValueChange = { step.label = it },
                    label = "Label (shown on this step's tile)",
                    modifier = Modifier.fillMaxWidth().padding(top = 6.dp)
                )
            }
        }

        iconPickerForIndex?.let { idx ->
            BuiltinIconPickerDialog(
                accentColor = accentColor,
                onDismiss = { iconPickerForIndex = null },
                onSelect = { name -> steps[idx].icon = "builtin:$name"; iconPickerForIndex = null }
            )
        }

        if (allowManualAdd) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 4.dp)) {
            InlineDropdownField(
                label = "Step type",
                selectedLabel = actionTypeLabels[newType] ?: newType,
                expanded = typeDropdownExpanded,
                onExpandedChange = { typeDropdownExpanded = it },
                modifier = Modifier.width(150.dp)
            ) {
                addableTypes.forEach { t ->
                    Text(
                        text = actionTypeLabels[t] ?: t,
                        color = MaterialTheme.colorScheme.onSurface,
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable {
                                newType = t
                                newValue = if (t == "media_control") "PlayPause" else ""
                                typeDropdownExpanded = false
                            }
                            .padding(horizontal = 16.dp, vertical = 12.dp)
                    )
                }
            }
            Spacer(modifier = Modifier.width(8.dp))
            if (newType == "media_control") {
                var addMediaDropdownExpanded by remember { mutableStateOf(false) }
                InlineDropdownField(
                    label = "Command",
                    selectedLabel = mediaCommandLabels[newValue] ?: "Select…",
                    expanded = addMediaDropdownExpanded,
                    onExpandedChange = { addMediaDropdownExpanded = it },
                    modifier = Modifier.weight(1f)
                ) {
                    mediaCommandLabels.forEach { (cmd, friendly) ->
                        Text(
                            text = friendly,
                            color = MaterialTheme.colorScheme.onSurface,
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable { newValue = cmd; addMediaDropdownExpanded = false }
                                .padding(horizontal = 16.dp, vertical = 12.dp)
                        )
                    }
                }
            } else {
                CrossDeckTextField(
                    value = newValue,
                    onValueChange = { newValue = it },
                    label = when (newType) {
                        "hotkey" -> "Keys (comma-separated)"
                        "launch_app", "open_folder" -> "Path"
                        "open_url" -> "URL"
                        "run_command" -> "Command"
                        else -> "Value"
                    },
                    modifier = Modifier.weight(1f)
                )
            }
            TextButton(onClick = {
                if (newValue.isNotBlank()) {
                    val step = StepUiState(ActionModel(type = newType), 0)
                    when (newType) {
                        "hotkey" -> step.keys = newValue
                        "launch_app" -> step.path = newValue
                        "open_folder" -> step.path = newValue
                        "media_control" -> step.mediaCommand = newValue
                        "open_url" -> step.url = newValue
                        "run_command" -> step.command = newValue
                        "text_snippet" -> step.text = newValue
                    }
                    steps.add(step)
                    newValue = if (newType == "media_control") "PlayPause" else ""
                }
            }) { Text("+ Add", color = accentColor) }
        }
        }
    }
}

/** Long-press's "one card per sub-button" editor — each entry gets the exact same rich
 * icon/label/type/parameter editor as a real top-level button (via a recursive ActionTypeEditor
 * call with allowChaining=false), not the compact row UI ActionStepListEditor uses. */
@Composable
private fun RichStepListEditor(
    steps: MutableList<ActionEditorState>,
    appList: List<com.crossdeck.client.model.DiscoveredApp>,
    onRequestAppList: () -> Unit,
    onRequestExtractIcon: ((String) -> Unit)?,
    accentColor: Color,
    connectedHostUrl: String?,
    authToken: String?,
    iconHashCache: Map<String, String>,
    onIconUpload: (suspend (ByteArray) -> String?)?,
    extractedIcon: Pair<String, String?>? = null,
) {
    Column {
        steps.forEachIndexed { index, subState ->
            Surface(
                shape = RoundedCornerShape(10.dp),
                color = MaterialTheme.colorScheme.surface.copy(alpha = 0.4f),
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(bottom = 12.dp)
                    .border(1.dp, MaterialTheme.colorScheme.onSurface.copy(alpha = 0.1f), RoundedCornerShape(10.dp))
            ) {
                Column(modifier = Modifier.padding(12.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            "Long-Press Button ${index + 1}",
                            style = MaterialTheme.typography.labelMedium.copy(fontWeight = FontWeight.Bold),
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Row {
                            IconButton(onClick = { if (index > 0) { val s = steps.removeAt(index); steps.add(index - 1, s) } }, enabled = index > 0) {
                                Icon(Icons.Default.KeyboardArrowUp, contentDescription = "Move up")
                            }
                            IconButton(onClick = { if (index < steps.size - 1) { val s = steps.removeAt(index); steps.add(index + 1, s) } }, enabled = index < steps.size - 1) {
                                Icon(Icons.Default.KeyboardArrowDown, contentDescription = "Move down")
                            }
                            IconButton(onClick = { steps.removeAt(index) }) {
                                Icon(Icons.Default.Close, contentDescription = "Remove long-press button", tint = MaterialTheme.colorScheme.error)
                            }
                        }
                    }
                    Spacer(modifier = Modifier.height(8.dp))
                    key(subState) {
                        ActionTypeEditor(
                            state = subState,
                            appList = appList,
                            onRequestAppList = onRequestAppList,
                            onRequestExtractIcon = onRequestExtractIcon,
                            accentColor = accentColor,
                            connectedHostUrl = connectedHostUrl,
                            authToken = authToken,
                            iconHashCache = iconHashCache,
                            showIconPicker = true,
                            onIconUpload = onIconUpload,
                            allowChaining = false,
                            extractedIcon = extractedIcon,
                        )
                    }
                }
            }
        }
        TextButton(onClick = { steps.add(ActionEditorState(null)) }, modifier = Modifier.fillMaxWidth()) {
            Text("+ Add Another Action", color = accentColor)
        }
    }
}

@Composable
private fun CrossDeckTextField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier,
    readOnly: Boolean = false,
    trailingIcon: @Composable (() -> Unit)? = null,
    keyboardOptions: androidx.compose.foundation.text.KeyboardOptions = androidx.compose.foundation.text.KeyboardOptions.Default
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        readOnly = readOnly,
        trailingIcon = trailingIcon,
        keyboardOptions = keyboardOptions,
        colors = androidx.compose.material3.OutlinedTextFieldDefaults.colors(
            focusedTextColor = MaterialTheme.colorScheme.onSurface,
            unfocusedTextColor = MaterialTheme.colorScheme.onSurface,
            focusedBorderColor = MaterialTheme.colorScheme.primary,
            unfocusedBorderColor = MaterialTheme.colorScheme.outline,
            focusedLabelColor = MaterialTheme.colorScheme.primary,
            unfocusedLabelColor = MaterialTheme.colorScheme.onSurfaceVariant,
            focusedContainerColor = MaterialTheme.colorScheme.surface,
            unfocusedContainerColor = MaterialTheme.colorScheme.surface
        ),
        shape = RoundedCornerShape(10.dp),
        modifier = modifier
    )
}

/**
 * Popup-free dropdown: expands inline instead of via ExposedDropdownMenuBox's ExposedDropdownMenu,
 * which is itself a Popup — nesting that inside EditButtonDialog's ModalBottomSheet (also a Popup)
 * broke the dropdown from opening at all. Options render as a plain Column directly below the
 * field, in the same composition/window layer as the sheet, so there's nothing to nest.
 */
@Composable
private fun InlineDropdownField(
    label: String,
    selectedLabel: String,
    expanded: Boolean,
    onExpandedChange: (Boolean) -> Unit,
    modifier: Modifier = Modifier,
    optionsContent: @Composable ColumnScope.() -> Unit
) {
    Column(modifier = modifier) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(bottom = 4.dp, start = 4.dp)
        )
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .clickable { onExpandedChange(!expanded) }
                .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(10.dp))
                .border(1.dp, MaterialTheme.colorScheme.outline, RoundedCornerShape(10.dp))
                .padding(horizontal = 16.dp, vertical = 14.dp)
        ) {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                Text(selectedLabel, color = MaterialTheme.colorScheme.onSurface, modifier = Modifier.weight(1f))
                Icon(
                    imageVector = if (expanded) Icons.Default.KeyboardArrowUp else Icons.Default.KeyboardArrowDown,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
        if (expanded) {
            Surface(
                shape = RoundedCornerShape(10.dp),
                color = MaterialTheme.colorScheme.surfaceVariant,
                modifier = Modifier.fillMaxWidth().padding(top = 4.dp)
            ) {
                Column(content = optionsContent)
            }
        }
    }
}
