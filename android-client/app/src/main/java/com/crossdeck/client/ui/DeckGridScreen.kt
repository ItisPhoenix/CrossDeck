package com.crossdeck.client.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.produceState
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.IconButton
import androidx.compose.ui.graphics.Brush
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.ui.input.pointer.pointerInput
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
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Snackbar
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.rememberScrollState
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
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.material.icons.filled.Add
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.animateFloat
import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
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
import com.crossdeck.client.model.Position
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
    accentColorHex: String,
    onAccentColorChange: (String) -> Unit,
    onButtonTap: (ButtonModel) -> Unit,
    onButtonSave: (ButtonModel) -> Unit,
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
    onDialAdjust: (String, Int?) -> Unit,
    settings: AppSettings,
    onSettingsChange: (AppSettings) -> Unit,
    connectionHostInfo: String?,
    onForgetHost: () -> Unit,
    onClearIconCache: () -> Unit,
    runningApps: List<com.crossdeck.client.model.RunningApp> = emptyList(),
    onRunningAppsSubscribe: (Boolean) -> Unit = {},
    onWindowFocus: (Long) -> Unit = {},
    onWindowClose: (Long) -> Unit = {},
    onButtonLongPress: (ButtonModel) -> Unit = {}
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

    var pendingRunCommandButton by remember { mutableStateOf<ButtonModel?>(null) }

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
    var creatingAtPosition by remember { mutableStateOf<Position?>(null) }

    var currentFolderId by remember { mutableStateOf<String?>(null) }
    var folderHistory by remember { mutableStateOf<List<Pair<String, String>>>(emptyList()) }
    var activeDialButton by remember { mutableStateOf<ButtonModel?>(null) }

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

    // Dynamic grid dimensions from the profile model (defaults to 3×5 if unset).
    val rows = profile.rows.coerceAtLeast(1)
    val cols = profile.columns.coerceAtLeast(1)
    val displayedButtons = profile.buttons.filter { it.parentFolderId == currentFolderId }
    val buttonMap = displayedButtons.associateBy { it.position.row to it.position.col }

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
                Snackbar(
                    snackbarData = data,
                    containerColor = if (isSuccess) Go else MaterialTheme.colorScheme.error,
                    contentColor = MaterialTheme.colorScheme.onSurface
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
            Column(modifier = Modifier.fillMaxSize()) {
                // Grid layout wrapper with 3D Flip capability
                var swipeAccum by remember { mutableStateOf(0f) }
                val canSwipeProfiles = profiles.size > 1 && currentFolderId == null && !isEditMode
                BoxWithConstraints(
                    contentAlignment = Alignment.Center,
                    modifier = Modifier
                        .fillMaxSize()
                        .weight(1f)
                        .padding(8.dp)
                        .then(
                            if (canSwipeProfiles) {
                                Modifier.pointerInput(profiles, activeProfileId) {
                                    detectHorizontalDragGestures(
                                        onDragStart = { swipeAccum = 0f },
                                        onDragEnd = {
                                            val idx = profiles.indexOfFirst { it.profileId == activeProfileId }
                                            if (idx >= 0) {
                                                val threshold = 120f
                                                if (swipeAccum <= -threshold) {
                                                    haptic()
                                                    onProfileSwitch(profiles[(idx + 1) % profiles.size].profileId)
                                                } else if (swipeAccum >= threshold) {
                                                    haptic()
                                                    onProfileSwitch(profiles[(idx - 1 + profiles.size) % profiles.size].profileId)
                                                }
                                            }
                                            swipeAccum = 0f
                                        }
                                    ) { change, dragAmount ->
                                        change.consume()
                                        swipeAccum += dragAmount
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
                        // Square cells sized to the tighter axis so the whole grid always fits with
                        // no scroll (portrait or landscape); the parent Box centers it.
                        val cellSize = remember(maxWidth, maxHeight, rows, cols, gridSpacing) {
                            val availableW = maxWidth - 4.dp - gridSpacing * (cols - 1)
                            val availableH = maxHeight - 4.dp - topInset - gridSpacing * (rows - 1)
                            minOf(availableW / cols, availableH / rows).coerceAtLeast(0.dp)
                        }

                        // Content-sized; the parent Box centers it in both axes.
                        Column(
                            verticalArrangement = Arrangement.spacedBy(gridSpacing),
                            modifier = Modifier.onGloballyPositioned { gridCoordinates = it }
                        ) {
                        for (r in 0 until rows) {
                        Row(horizontalArrangement = Arrangement.spacedBy(gridSpacing)) {
                        for (c in 0 until cols) {
                                val index = r * cols + c
                                val cellButton = buttonMap[r to c]
                                val isDraggingThis = draggedIndex == index

                                val dragModifier = if (isEditMode && cellButton != null) {
                                    // Keyed on buttonMap, not just index: pointerInput's gesture
                                    // coroutine captures buttonMap once per key change, and index
                                    // never changes for a cell — after any move/edit the handler
                                    // held a stale map, so onDragEnd saved whatever button *used*
                                    // to be at that cell ("drag copies a random button" bug).
                                    Modifier.pointerInput(index, buttonMap) {
                                        detectDragGesturesAfterLongPress(
                                            onDragStart = {
                                                haptic()
                                                draggedIndex = index
                                                dragOffset = Offset.Zero
                                            },
                                            onDrag = { change, dragAmount ->
                                                change.consume()
                                                dragOffset += dragAmount
                                            },
                                            onDragEnd = {
                                                val coords = gridCoordinates
                                                val startIdx = draggedIndex
                                                if (coords != null && startIdx != null) {
                                                    val cellWidthPx = coords.size.width / cols
                                                    val cellHeightPx = coords.size.height / rows

                                                    val startR = startIdx / cols
                                                    val startC = startIdx % cols

                                                    val startX = startC * cellWidthPx
                                                    val startY = startR * cellHeightPx

                                                    val touchX = startX + cellWidthPx / 2 + dragOffset.x
                                                    val touchY = startY + cellHeightPx / 2 + dragOffset.y

                                                    val targetC = (touchX / cellWidthPx).toInt().coerceIn(0, cols - 1)
                                                    val targetR = (touchY / cellHeightPx).toInt().coerceIn(0, rows - 1)
                                                    val targetIdx = targetR * cols + targetC
                                                    
                                                    if (targetIdx != startIdx) {
                                                        haptic()
                                                        val draggedBtn = buttonMap[startR to startC]
                                                        val targetBtn = buttonMap[targetR to targetC]
                                                        if (draggedBtn != null) {
                                                            if (targetBtn != null) {
                                                                onButtonSave(draggedBtn.copy(position = Position(targetR, targetC)))
                                                                onButtonSave(targetBtn.copy(position = Position(startR, startC)))
                                                            } else {
                                                                onButtonSave(draggedBtn.copy(position = Position(targetR, targetC)))
                                                            }
                                                        }
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
                                        val dialValue = dialLevels[cellButton.buttonId]
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
                                                if (isEditMode) {
                                                    editingButton = cellButton
                                                } else {
                                                    if (cellButton.action.type == "open_folder") {
                                                        val destFolder = cellButton.action.targetFolderId
                                                        if (destFolder != null) {
                                                            folderHistory =
                                                                folderHistory + ((currentFolderId ?: "") to cellButton.label)
                                                            currentFolderId = destFolder
                                                        }
                                                    } else if (cellButton.action.type == "dial") {
                                                        activeDialButton = cellButton
                                                        onDialAdjust(cellButton.buttonId, null) // fetch current level
                                                    } else if (cellButton.action.type == "run_command" && settings.confirmRunCommand) {
                                                        pendingRunCommandButton = cellButton
                                                    } else {
                                                        onButtonTap(cellButton)
                                                    }
                                                }
                                            },
                                            onLongPress = if (!isEditMode && cellButton.longPressAction != null) {
                                                {
                                                    haptic(HapticFeedbackConstants.LONG_PRESS)
                                                    onButtonLongPress(cellButton)
                                                }
                                            } else null,
                                            modifier = dragModifier.then(visualModifier),
                                            levelValue = dialValue
                                        )
                                    } else {
                                        if (isEditMode) {
                                            EmptyEditButton(
                                                onClick = {
                                                    haptic()
                                                    creatingAtPosition = Position(r, c)
                                                }
                                            )
                                        } else {
                                            // Empty cell pulse ring — a neon ring that slowly
                                            // pulses in opacity so the deck feels alive.
                                            val emptyPulse by rememberInfiniteTransition(label = "emptyPulse")
                                                .animateFloat(
                                                    initialValue = 0.15f,
                                                    targetValue = 0.65f,
                                                    animationSpec = androidx.compose.animation.core.infiniteRepeatable(
                                                        animation = tween(2000, easing = androidx.compose.animation.core.LinearEasing),
                                                        repeatMode = androidx.compose.animation.core.RepeatMode.Reverse
                                                    ),
                                                    label = "emptyPulseAlpha"
                                                )
                                            Box(
                                                modifier = Modifier
                                                    .fillMaxSize()
                                                    .background(
                                                        Brush.verticalGradient(
                                                            listOf(
                                                                MaterialTheme.colorScheme.surface.copy(alpha = 0.5f),
                                                                MaterialTheme.colorScheme.background.copy(alpha = 0.5f)
                                                            )
                                                        ),
                                                        RoundedCornerShape(18.dp)
                                                    )
                                                    .border(
                                                        1.2.dp,
                                                        Brush.verticalGradient(
                                                            listOf(
                                                                MaterialTheme.colorScheme.onSurface.copy(alpha = 0.12f),
                                                                MaterialTheme.colorScheme.onSurface.copy(alpha = 0.02f)
                                                            )
                                                        ),
                                                        RoundedCornerShape(18.dp)
                                                    ),
                                                contentAlignment = Alignment.Center
                                            ) {
                                                Box(
                                                    modifier = Modifier
                                                        .size(26.dp)
                                                        .border(1.5.dp, accentColor.copy(alpha = emptyPulse), RoundedCornerShape(50))
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
                                imageVector = Icons.Default.Add,
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
                        text = { Text("+ New Profile", color = accentColor) },
                        onClick = {
                            expanded = false
                            showCreateDialog = true
                        }
                    )
                    DropdownMenuItem(
                        text = { Text("✏ Rename Current") },
                        onClick = {
                            expanded = false
                            renameProfileName = profile.name
                            showRenameDialog = true
                        }
                    )
                    if (profiles.size > 1) {
                        DropdownMenuItem(
                            text = { Text("🗑 Delete Current", color = MaterialTheme.colorScheme.error) },
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
                    onDismiss = { showRunningApps = false }
                )
            }

            // Confirm-before-Run-Command safety prompt

            if (pendingRunCommandButton != null) {
                val button = pendingRunCommandButton!!
                AlertDialog(
                    onDismissRequest = { pendingRunCommandButton = null },
                    modifier = Modifier.border(1.dp, accentColor.copy(alpha = 0.5f), RoundedCornerShape(16.dp)),
                    containerColor = MaterialTheme.colorScheme.surface,
                    shape = RoundedCornerShape(16.dp),
                    title = { Text("Run Command?", color = MaterialTheme.colorScheme.onSurface) },
                    text = { Text("Run '${button.action.command}'?", color = MaterialTheme.colorScheme.onSurfaceVariant) },
                    confirmButton = {
                        TextButton(onClick = {
                            onButtonTap(button)
                            pendingRunCommandButton = null
                        }) {
                            Text("Run", color = MaterialTheme.colorScheme.error)
                        }
                    },
                    dismissButton = {
                        TextButton(onClick = { pendingRunCommandButton = null }) {
                            Text("Cancel", color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                )
            }

            // Thick fluid bottom-sheet touch-bar slider modal for dials
            if (activeDialButton != null) {
                val button = activeDialButton!!
                val currentLevel = dialLevels[button.buttonId] ?: 50
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
                        Text(button.label, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
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
                                    onDialAdjust(button.buttonId, newValue.toInt())
                                    lastSentValue = newValue.toInt()
                                }
                            },
                            onValueChangeFinished = {
                                isUserDragging = false
                                haptic()
                                onDialAdjust(button.buttonId, localSliderValue.toInt())
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
            if (creatingAtPosition != null) {
                val newButtonPlaceholder = ButtonModel(
                    buttonId = "b_" + UUID.randomUUID().toString().substring(0, 8),
                    position = creatingAtPosition!!,
                    label = "",
                    action = ActionModel(type = "hotkey"),
                    parentFolderId = currentFolderId
                )
                EditButtonDialog(
                    button = newButtonPlaceholder,
                    connectedHostUrl = connectedHostUrl,
                    authToken = authToken,
                    onIconUpload = onIconUpload,
                    appList = appList,
                    onRequestAppList = onRequestAppList,
                    extractedIcon = extractedIcon,
                    onRequestExtractIcon = onRequestExtractIcon,
                    onDismiss = { creatingAtPosition = null },
                    onSave = { savedButton ->
                        onButtonSave(savedButton)
                        creatingAtPosition = null
                    },
                    onDelete = null
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
                    }
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
    val isPressed by interactionSource.collectIsPressedAsState()
    val animatedScale by animateFloatAsState(targetValue = if (isPressed) 0.94f else 1.0f, label = "pressScale")

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

    Surface(
        shape = RoundedCornerShape(18.dp),
        color = Color.Transparent,
        modifier = modifier
            .fillMaxSize()
            .scale(animatedScale)
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
            // Ripple ring that expands + fades on press, giving tactile feedback.
            val rippleScale by animateFloatAsState(
                targetValue = if (isPressed) 1.6f else 0.6f,
                animationSpec = tween(durationMillis = if (isPressed) 350 else 200),
                label = "rippleScale"
            )
            val rippleAlpha by animateFloatAsState(
                targetValue = if (isPressed) 0.45f else 0f,
                animationSpec = tween(durationMillis = if (isPressed) 350 else 200),
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
                        modifier = Modifier.size(if (iconOnlyMode) 48.dp else 38.dp).padding(bottom = 2.dp)
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
                                Column(
                                    horizontalAlignment = Alignment.CenterHorizontally,
                                    verticalArrangement = Arrangement.Center,
                                    modifier = Modifier
                                        .size(cellSize)
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
    "dial" to "Dial / Slider"
)
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
    "brightness" to "Brightness"
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
    onDelete: (() -> Unit)?
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var iconValue by remember { mutableStateOf(button.icon) }
    var showBuiltinPicker by remember { mutableStateOf(false) }
    var isUploading by remember { mutableStateOf(false) }
    var pathDropdownExpanded by remember { mutableStateOf(false) }

    val imagePickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.GetContent()
    ) { uri: android.net.Uri? ->
        if (uri == null) return@rememberLauncherForActivityResult
        scope.launch {
            isUploading = true
            try {
                val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
                if (bytes != null) {
                    onIconUpload(bytes)?.let { hash -> iconValue = hash }
                }
            } finally {
                isUploading = false
            }
        }
    }

    var label by remember { mutableStateOf(button.label) }
    var actionType by remember { mutableStateOf(button.action.type) }
    var hotkeys by remember { mutableStateOf(button.action.keys?.joinToString(",") ?: "") }
    var path by remember { mutableStateOf(button.action.path ?: "") }
    var mediaCommand by remember { mutableStateOf(button.action.mediaCommand ?: "PlayPause") }
    var url by remember { mutableStateOf(button.action.url ?: "") }
    var command by remember { mutableStateOf(button.action.command ?: "") }
    var textValue by remember { mutableStateOf(button.action.text ?: "") }
    var targetFolderId by remember { mutableStateOf(button.action.targetFolderId ?: ("f_" + UUID.randomUUID().toString().substring(0, 8))) }
    var multiActionText by remember { mutableStateOf(formatMultiAction(button.action.actions, button.action.delays)) }
    var dialTarget by remember { mutableStateOf(button.action.dialTarget ?: "volume") }
    var longPressText by remember {
        mutableStateOf(button.longPressAction?.let { lp ->
            if (lp.type == "multi_action") formatMultiAction(lp.actions, lp.delays)
            else formatMultiAction(listOf(lp), null)
        } ?: "")
    }
    
    var dropdownExpanded by remember { mutableStateOf(false) }
    var mediaDropdownExpanded by remember { mutableStateOf(false) }
    var dialDropdownExpanded by remember { mutableStateOf(false) }

    var searchQuery by remember { mutableStateOf("") }
    LaunchedEffect(path, appList) {
        val matched = appList.find { it.path == path }
        searchQuery = matched?.name ?: path
    }

    // Fetch the installed-apps list only once the user is actually on the Launch App type —
    // mirrors the PC editor's lazy-ish approach (scan cost only paid when relevant).
    LaunchedEffect(actionType) {
        if (actionType == "launch_app") onRequestAppList()
    }
    // Auto-icon-on-select (mirrors the PC editor): when the host responds to an extract_icon
    // request for the path currently in the field, and no icon is set yet, use it.
    LaunchedEffect(extractedIcon) {
        val (extractedPath, extractedHash) = extractedIcon ?: return@LaunchedEffect
        if ((extractedPath == path || (actionType == "open_url" && extractedPath == url)) && extractedHash != null && iconValue == null) {
            iconValue = extractedHash
        }
    }

    LaunchedEffect(url, actionType) {
        if (actionType == "open_url" && url.isNotBlank()) {
            kotlinx.coroutines.delay(700)
            onRequestExtractIcon(url)
        }
    }

    // launch_app's path never triggered an extract request on its own — only open_url did —
    // so picking/typing an app path silently never fetched its icon. Mirror the open_url effect.
    LaunchedEffect(path, actionType) {
        if (actionType == "launch_app" && path.isNotBlank()) {
            kotlinx.coroutines.delay(400)
            onRequestExtractIcon(path)
        }
    }

    val accentColor = MaterialTheme.colorScheme.primary

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
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
        windowInsets = WindowInsets(0)
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .imePadding()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 20.dp, vertical = 8.dp)
        ) {
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
                    onClick = {
                        val act = when (actionType) {
                            "hotkey" -> ActionModel(
                                type = actionType,
                                keys = hotkeys.split(",").map { it.trim() }.filter { it.isNotEmpty() }
                            )
                            "launch_app" -> ActionModel(
                                type = actionType,
                                path = path.trim()
                            )
                            "media_control" -> ActionModel(
                                type = actionType,
                                mediaCommand = mediaCommand
                            )
                            "open_url" -> ActionModel(
                                type = actionType,
                                url = url.trim()
                            )
                            "run_command" -> ActionModel(
                                type = actionType,
                                command = command.trim()
                            )
                            "text_snippet" -> ActionModel(
                                type = actionType,
                                text = textValue
                            )
                            "open_folder" -> ActionModel(
                                type = actionType,
                                targetFolderId = targetFolderId.trim()
                            )
                            "multi_action" -> {
                                val (parsedActs, parsedDelays) = parseMultiAction(multiActionText)
                                ActionModel(
                                    type = actionType,
                                    actions = parsedActs,
                                    delays = parsedDelays
                                )
                            }
                            "dial" -> ActionModel(
                                type = actionType,
                                dialTarget = dialTarget
                            )
                            else -> ActionModel(type = actionType)
                        }
                        val (lpActs, lpDelays) = parseMultiAction(longPressText)
                        val longPress = when (lpActs.size) {
                            0 -> null
                            1 -> lpActs[0]
                            else -> ActionModel(type = "multi_action", actions = lpActs, delays = lpDelays)
                        }
                        onSave(button.copy(label = label.trim(), icon = iconValue, action = act, longPressAction = longPress))
                    }
                ) {
                    Text("Save", color = accentColor, fontWeight = FontWeight.Bold)
                }
            }

            CrossDeckTextField(
                value = label,
                onValueChange = { label = it },
                label = "Label",
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(modifier = Modifier.height(16.dp))

            Text("Icon", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 4.dp)) {
                IconPreview(icon = iconValue, connectedHostUrl = connectedHostUrl, authToken = authToken, modifier = Modifier.size(40.dp))
                Spacer(modifier = Modifier.width(8.dp))
                TextButton(onClick = { showBuiltinPicker = true }) { Text("Built-in", color = accentColor) }
                TextButton(onClick = { imagePickerLauncher.launch("image/*") }, enabled = !isUploading) {
                    Text(if (isUploading) "Uploading…" else "Upload", color = accentColor)
                }
                if (iconValue != null) {
                    TextButton(onClick = { iconValue = null }) { Text("Clear", color = MaterialTheme.colorScheme.error) }
                }
            }
            Spacer(modifier = Modifier.height(16.dp))

            InlineDropdownField(
                label = "Action Type",
                selectedLabel = actionTypeLabels[actionType] ?: actionType,
                expanded = dropdownExpanded,
                onExpandedChange = { dropdownExpanded = it },
                modifier = Modifier.fillMaxWidth()
            ) {
                actionTypeLabels.forEach { (type, friendly) ->
                    Text(
                        text = friendly,
                        color = MaterialTheme.colorScheme.onSurface,
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable {
                                actionType = type
                                dropdownExpanded = false
                            }
                            .padding(horizontal = 16.dp, vertical = 12.dp)
                    )
                }
            }
            Spacer(modifier = Modifier.height(16.dp))

            // Action Details Collapsible Panel
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
                    when (actionType) {
                        "hotkey" -> {
                            CrossDeckTextField(
                                value = hotkeys,
                                onValueChange = { hotkeys = it },
                                label = "Keys (comma-separated, e.g. Ctrl,Alt,A)",
                                modifier = Modifier.fillMaxWidth()
                            )
                        }
                        "launch_app" -> {
                            val filteredApps = remember(searchQuery, path, appList) {
                                val matched = appList.find { it.path == path }
                                val isDefaultValue = searchQuery.isBlank() || searchQuery == path || (matched != null && searchQuery == matched.name)
                                if (isDefaultValue) appList
                                else appList.filter { 
                                    it.name.contains(searchQuery, ignoreCase = true) || 
                                    it.path.contains(searchQuery, ignoreCase = true) 
                                }
                            }
                            CrossDeckTextField(
                                value = searchQuery,
                                onValueChange = { newValue ->
                                    searchQuery = newValue
                                    val matched = appList.find { it.name.equals(newValue, ignoreCase = true) }
                                    path = matched?.path ?: newValue
                                    pathDropdownExpanded = true
                                },
                                label = "Application Path (pick or type custom)",
                                modifier = Modifier.fillMaxWidth()
                            )
                            // Inline, not a Popup-based ExposedDropdownMenu — that broke inside
                            // this ModalBottomSheet (Popup nested in a Popup).
                            if (pathDropdownExpanded && filteredApps.isNotEmpty()) {
                                Surface(
                                    shape = RoundedCornerShape(10.dp),
                                    color = MaterialTheme.colorScheme.surfaceVariant,
                                    modifier = Modifier.fillMaxWidth().padding(top = 4.dp)
                                ) {
                                    Column {
                                        filteredApps.forEach { app ->
                                            Text(
                                                text = app.name,
                                                color = MaterialTheme.colorScheme.onSurface,
                                                modifier = Modifier
                                                    .fillMaxWidth()
                                                    .clickable {
                                                        path = app.path
                                                        searchQuery = app.name
                                                        pathDropdownExpanded = false
                                                        if (iconValue == null) onRequestExtractIcon(app.path)
                                                    }
                                                    .padding(horizontal = 16.dp, vertical = 12.dp)
                                            )
                                        }
                                    }
                                }
                            }
                            if (path.isNotEmpty() && path != searchQuery) {
                                Spacer(modifier = Modifier.height(4.dp))
                                Text(
                                    text = "Target: $path",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    modifier = Modifier.padding(horizontal = 4.dp)
                                )
                            }
                        }
                        "media_control" -> {
                            InlineDropdownField(
                                label = "Media Command",
                                selectedLabel = mediaCommandLabels[mediaCommand] ?: mediaCommand,
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
                                            .clickable {
                                                mediaCommand = cmd
                                                mediaDropdownExpanded = false
                                            }
                                            .padding(horizontal = 16.dp, vertical = 12.dp)
                                    )
                                }
                            }
                        }
                        "open_url" -> {
                            CrossDeckTextField(
                                value = url,
                                onValueChange = { url = it },
                                label = "URL",
                                modifier = Modifier.fillMaxWidth()
                            )
                        }
                        "run_command" -> {
                            CrossDeckTextField(
                                value = command,
                                onValueChange = { command = it },
                                label = "Command",
                                modifier = Modifier.fillMaxWidth()
                            )
                        }
                        "text_snippet" -> {
                            CrossDeckTextField(
                                value = textValue,
                                onValueChange = { textValue = it },
                                label = "Text Snippet",
                                modifier = Modifier.fillMaxWidth()
                            )
                        }
                        "open_folder" -> {
                            CrossDeckTextField(
                                value = targetFolderId,
                                onValueChange = { targetFolderId = it },
                                label = "Target Folder ID",
                                modifier = Modifier.fillMaxWidth()
                            )
                        }
                        "multi_action" -> {
                            CrossDeckTextField(
                                value = multiActionText,
                                onValueChange = { multiActionText = it },
                                label = "Action chain — one step per line",
                                modifier = Modifier.fillMaxWidth()
                            )
                            // Tap-to-append starter lines so nobody has to memorize the format.
                            Row(modifier = Modifier.padding(top = 4.dp)) {
                                listOf(
                                    "+ Shortcut" to "Keyboard Shortcut: Ctrl,C",
                                    "+ Media" to "Media Control: PlayPause",
                                    "+ Delay" to "Delay (ms): 500"
                                ).forEach { (chip, line) ->
                                    TextButton(onClick = {
                                        multiActionText = if (multiActionText.isBlank()) line
                                            else multiActionText.trimEnd() + "\n" + line
                                    }) { Text(chip, color = accentColor, style = MaterialTheme.typography.bodySmall) }
                                }
                            }
                        }
                        "dial" -> {
                            InlineDropdownField(
                                label = "Dial Target",
                                selectedLabel = dialTargetLabels[dialTarget] ?: dialTarget,
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
                                            .clickable {
                                                dialTarget = target
                                                dialDropdownExpanded = false
                                            }
                                            .padding(horizontal = 16.dp, vertical = 12.dp)
                                    )
                                }
                            }
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.height(16.dp))
            CrossDeckTextField(
                value = longPressText,
                onValueChange = { longPressText = it },
                label = "Long-press action (optional, e.g. Media Control: VolumeMute)",
                modifier = Modifier.fillMaxWidth()
            )

            if (onDelete != null) {
                Spacer(modifier = Modifier.height(20.dp))
                Button(
                    onClick = onDelete,
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
            onDismiss = { showBuiltinPicker = false },
            onSelect = { name ->
                iconValue = "builtin:$name"
                showBuiltinPicker = false
            }
        )
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
private fun BuiltinIconPickerDialog(onDismiss: () -> Unit, onSelect: (String) -> Unit) {
    val context = LocalContext.current
    var names by remember { mutableStateOf<List<String>>(emptyList()) }

    LaunchedEffect(Unit) {
        names = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            context.assets.list("builtin")
                ?.filter { it.endsWith(".png") }
                ?.map { it.removeSuffix(".png") }
                ?.sorted()
                ?: emptyList()
        }
    }

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
            LazyVerticalGrid(
                columns = GridCells.Fixed(5),
                modifier = Modifier.height(360.dp)
            ) {
                items(names) { name ->
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
                    Box(
                        modifier = Modifier
                            .padding(4.dp)
                            .size(48.dp)
                            .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(6.dp))
                            .border(1.dp, MaterialTheme.colorScheme.outline, RoundedCornerShape(6.dp))
                            .clickable { onSelect(name) },
                        contentAlignment = Alignment.Center
                    ) {
                        bitmap?.let { Image(bitmap = it, contentDescription = name, modifier = Modifier.size(28.dp)) }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) { Text("Cancel", color = MaterialTheme.colorScheme.onSurfaceVariant) }
        }
    )
}

private fun formatMultiAction(actions: List<ActionModel>?, delays: List<Int>?): String {
    if (actions == null) return ""
    val sb = StringBuilder()
    for (i in actions.indices) {
        val act = actions[i]
        val label = actionTypeLabels[act.type]
        if (label != null) {
            when (act.type) {
                "hotkey" -> sb.append("$label:${act.keys?.joinToString(",") ?: ""}")
                "launch_app" -> sb.append("$label:${act.path ?: ""}")
                "media_control" -> sb.append("$label:${act.mediaCommand ?: ""}")
                "open_url" -> sb.append("$label:${act.url ?: ""}")
                "run_command" -> sb.append("$label:${act.command ?: ""}")
                "text_snippet" -> sb.append("$label:${act.text ?: ""}")
            }
        }
        if (delays != null && i < delays.size && delays[i] > 0) {
            sb.append(" then delay:${delays[i]}")
        }
        if (i < actions.size - 1) {
            sb.append(" then ")
        }
    }
    return sb.toString()
}

private fun parseMultiAction(text: String): Pair<List<ActionModel>, List<Int>> {
    val actions = mutableListOf<ActionModel>()
    val delays = mutableListOf<Int>()
    val parts = text.split(" then ")
    for (part in parts) {
        val clean = part.trim()
        if (clean.startsWith("delay:")) {
            val dVal = clean.removePrefix("delay:").toIntOrNull() ?: 0
            if (delays.isNotEmpty()) {
                delays[delays.size - 1] = dVal
            }
        } else {
            val colonIdx = clean.indexOf(":")
            if (colonIdx != -1) {
                val enteredLabel = clean.substring(0, colonIdx).trim()
                val type = actionTypeLabels.entries.firstOrNull { it.value.equals(enteredLabel, ignoreCase = true) }?.key ?: enteredLabel
                val valStr = clean.substring(colonIdx + 1).trim()
                val act = when (type) {
                    "hotkey" -> ActionModel(type = type, keys = valStr.split(",").map { it.trim() })
                    "launch_app" -> ActionModel(type = type, path = valStr)
                    "media_control" -> ActionModel(type = type, mediaCommand = valStr)
                    "open_url" -> ActionModel(type = type, url = valStr)
                    "run_command" -> ActionModel(type = type, command = valStr)
                    "text_snippet" -> ActionModel(type = type, text = valStr)
                    else -> ActionModel(type = type)
                }
                actions.add(act)
                delays.add(0) // default delay placeholder
            }
        }
    }
    return actions to delays
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
