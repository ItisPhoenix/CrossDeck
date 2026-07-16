package com.crossdeck.client.ui

import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Snackbar
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextField
import androidx.compose.material3.Slider
import androidx.compose.runtime.Composable
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
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInWindow
import androidx.compose.ui.unit.IntOffset
import androidx.compose.foundation.layout.offset
import androidx.compose.ui.zIndex
import androidx.compose.ui.draw.scale
import androidx.compose.ui.draw.shadow
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.ui.input.pointer.PointerInputChange
import com.crossdeck.client.model.ActionModel
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
    /** Pair(message, isSuccess) — non-null for ~2.5 s when a toast should show. */
    toastMessage: Pair<String, Boolean>?,
    dialLevels: Map<String, Int>,
    onButtonTap: (ButtonModel) -> Unit,
    onButtonSave: (ButtonModel) -> Unit,
    onButtonDelete: (String) -> Unit,
    onProfileSwitch: (String) -> Unit,
    onProfileCreate: (String) -> Unit,
    onProfileDelete: (String) -> Unit,
    onProfileRename: (String, String) -> Unit,
    onDialAdjust: (String, Int?) -> Unit
) {
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()

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

    var draggedButtonId by remember { mutableStateOf<String?>(null) }
    var dragOffset by remember { mutableStateOf(androidx.compose.ui.geometry.Offset.Zero) }
    var draggedItemSize by remember { mutableStateOf(androidx.compose.ui.unit.IntSize.Zero) }

    var expanded by remember { mutableStateOf(false) }
    var showCreateDialog by remember { mutableStateOf(false) }
    var newProfileName by remember { mutableStateOf("") }
    var showRenameDialog by remember { mutableStateOf(false) }
    var renameProfileName by remember { mutableStateOf(profile.name) }

    androidx.compose.runtime.LaunchedEffect(activeProfileId) {
        currentFolderId = null
        folderHistory = emptyList()
    }

    val rows = 3
    val cols = 5
    val displayedButtons = profile.buttons.filter { it.parentFolderId == currentFolderId }
    val buttonMap = displayedButtons.associateBy { it.position.row to it.position.col }

    if (showCreateDialog) {
        AlertDialog(
            onDismissRequest = { showCreateDialog = false },
            title = { Text("Create New Profile") },
            text = {
                OutlinedTextField(
                    value = newProfileName,
                    onValueChange = { newProfileName = it },
                    label = { Text("Profile Name") }
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
                    Text("Create")
                }
            },
            dismissButton = {
                TextButton(onClick = { showCreateDialog = false }) {
                    Text("Cancel")
                }
            }
        )
    }

    if (showRenameDialog) {
        AlertDialog(
            onDismissRequest = { showRenameDialog = false },
            title = { Text("Rename Profile") },
            text = {
                OutlinedTextField(
                    value = renameProfileName,
                    onValueChange = { renameProfileName = it },
                    label = { Text("New Profile Name") }
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
                    Text("Rename")
                }
            },
            dismissButton = {
                TextButton(onClick = { showRenameDialog = false }) {
                    Text("Cancel")
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
                    containerColor = if (isSuccess)
                        Color(0xFF1B8040)  // dark green
                    else
                        Color(0xFFC62828), // dark red
                    contentColor = Color.White
                )
            }
        }
    ) { innerPadding ->
    Column(modifier = Modifier.fillMaxSize().padding(innerPadding)) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            ExposedDropdownMenuBox(
                expanded = expanded,
                onExpandedChange = { expanded = !expanded }
            ) {
                OutlinedTextField(
                    readOnly = true,
                    value = profile.name,
                    onValueChange = {},
                    label = { Text("Profile") },
                    trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = expanded) },
                    colors = ExposedDropdownMenuDefaults.outlinedTextFieldColors(),
                    modifier = Modifier.menuAnchor().width(180.dp)
                )
                ExposedDropdownMenu(
                    expanded = expanded,
                    onDismissRequest = { expanded = false }
                ) {
                    profiles.forEach { p ->
                        DropdownMenuItem(
                            text = { Text(p.name) },
                            onClick = {
                                expanded = false
                                onProfileSwitch(p.profileId)
                            }
                        )
                    }
                    DropdownMenuItem(
                        text = { Text("+ New Profile", color = MaterialTheme.colorScheme.primary) },
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
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("Edit Mode", style = MaterialTheme.typography.bodyMedium)
                Spacer(modifier = Modifier.width(8.dp))
                Switch(
                    checked = isEditMode,
                    onCheckedChange = { isEditMode = it }
                )
            }
        }

        if (currentFolderId != null) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                TextButton(onClick = {
                    val nextHistory = folderHistory.dropLast(1)
                    currentFolderId = nextHistory.lastOrNull()?.first
                    folderHistory = nextHistory
                }) {
                    Text("◀ Back", style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.primary)
                }
                Spacer(modifier = Modifier.width(8.dp))
                val folderLabel = folderHistory.lastOrNull()?.second ?: ""
                Text(
                    text = "Folder: $folderLabel",
                    style = MaterialTheme.typography.titleMedium
                )
            }
        }

        LazyVerticalGrid(
            columns = GridCells.Fixed(cols),
            contentPadding = PaddingValues(12.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
            modifier = Modifier.fillMaxSize()
        ) {
            items(rows * cols) { index ->
                val r = index / cols
                val c = index % cols
                val button = buttonMap[r to c]
                val isCurrentDragged = draggedButtonId == button?.buttonId

                val dragModifier = if (isEditMode && button != null) {
                    Modifier
                        .onGloballyPositioned { layoutCoords ->
                            if (isCurrentDragged) {
                                draggedItemSize = layoutCoords.size
                            }
                        }
                        .pointerInput(button.buttonId) {
                            detectDragGesturesAfterLongPress(
                                onDragStart = {
                                    draggedButtonId = button.buttonId
                                    dragOffset = androidx.compose.ui.geometry.Offset.Zero
                                },
                                onDrag = { change, dragAmount ->
                                    change.consume()
                                    dragOffset += dragAmount
                                },
                                onDragEnd = {
                                    val itemW = draggedItemSize.width.toFloat()
                                    val itemH = draggedItemSize.height.toFloat()
                                    if (itemW > 0 && itemH > 0) {
                                        val deltaCols = Math.round(dragOffset.x / itemW).toInt()
                                        val deltaRows = Math.round(dragOffset.y / itemH).toInt()
                                        val targetR = r + deltaRows
                                        val targetC = c + deltaCols
                                        if (targetR in 0 until rows && targetC in 0 until cols && (targetR != r || targetC != c)) {
                                            val targetButton = buttonMap[targetR to targetC]
                                            if (targetButton != null) {
                                                // Swap buttons
                                                onButtonSave(button.copy(position = Position(targetR, targetC)))
                                                onButtonSave(targetButton.copy(position = Position(r, c)))
                                            } else {
                                                // Move to empty space
                                                onButtonSave(button.copy(position = Position(targetR, targetC)))
                                            }
                                        }
                                    }
                                    draggedButtonId = null
                                    dragOffset = androidx.compose.ui.geometry.Offset.Zero
                                },
                                onDragCancel = {
                                    draggedButtonId = null
                                    dragOffset = androidx.compose.ui.geometry.Offset.Zero
                                }
                            )
                        }
                        .then(
                            if (isCurrentDragged) {
                                Modifier
                                    .zIndex(2f)
                                    .scale(1.1f)
                                    .shadow(8.dp, RoundedCornerShape(12.dp))
                                    .offset { IntOffset(dragOffset.x.roundToInt(), dragOffset.y.roundToInt()) }
                            } else Modifier
                        )
                } else Modifier

                if (button != null) {
                    DeckButton(
                        button = button,
                        isEditMode = isEditMode,
                        onTap = {
                            if (isEditMode) {
                                editingButton = button
                            } else {
                                if (button.action.type == "open_folder" && button.action.targetFolderId != null) {
                                    currentFolderId = button.action.targetFolderId
                                    folderHistory = folderHistory + (button.action.targetFolderId to button.label)
                                } else if (button.action.type == "dial") {
                                    activeDialButton = button
                                } else {
                                    onButtonTap(button)
                                }
                            }
                        },
                        modifier = dragModifier,
                        levelValue = dialLevels[button.buttonId]
                    )
                } else if (isEditMode) {
                    EmptyEditButton(onClick = {
                        creatingAtPosition = Position(r, c)
                    })
                } else {
                    Box(modifier = Modifier.aspectRatio(1f))
                }
            }
        }
    }

    editingButton?.let { button ->
        EditButtonDialog(
            button = button,
            onDismiss = { editingButton = null },
            onSave = { updated ->
                onButtonSave(updated)
                editingButton = null
            },
            onDelete = {
                onButtonDelete(button.buttonId)
                editingButton = null
            }
        )
    }

    creatingAtPosition?.let { pos ->
        val emptyButton = ButtonModel(
            buttonId = "b_" + UUID.randomUUID().toString().substring(0, 8),
            position = pos,
            label = "",
            action = ActionModel(type = "hotkey", keys = emptyList()),
            parentFolderId = currentFolderId
        )
        EditButtonDialog(
            button = emptyButton,
            onDismiss = { creatingAtPosition = null },
            onSave = { newBtn ->
                onButtonSave(newBtn)
                creatingAtPosition = null
            },
            onDelete = null
        )
    }
    
    activeDialButton?.let { button ->
        LaunchedEffect(button.buttonId) {
            onDialAdjust(button.buttonId, null) // Send null value to query current state
        }
        val currentLevel = dialLevels[button.buttonId] ?: 50
        var localSliderValue by remember { mutableStateOf(currentLevel.toFloat()) }
        var lastSentValue by remember { mutableStateOf(currentLevel) }
        var isUserDragging by remember { mutableStateOf(false) }

        // Sync local value with server state if server changes externally (and user is not dragging)
        LaunchedEffect(currentLevel) {
            if (!isUserDragging) {
                localSliderValue = currentLevel.toFloat()
                lastSentValue = currentLevel
            }
        }

        val labelIcon = if (button.action.dialTarget == "brightness") "☀️" else "🔊"
        AlertDialog(
            onDismissRequest = { activeDialButton = null },
            title = { Text(button.label) },
            text = {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(
                        text = "$labelIcon ${localSliderValue.toInt()}%",
                        style = MaterialTheme.typography.headlineMedium,
                        fontWeight = androidx.compose.ui.text.font.FontWeight.Bold
                    )
                    Spacer(modifier = Modifier.height(16.dp))
                    Slider(
                        value = localSliderValue,
                        onValueChange = { newValue ->
                            isUserDragging = true
                            localSliderValue = newValue
                            if (Math.abs(newValue.toInt() - lastSentValue) >= 4) {
                                onDialAdjust(button.buttonId, newValue.toInt())
                                lastSentValue = newValue.toInt()
                            }
                        },
                        onValueChangeFinished = {
                            isUserDragging = false
                            onDialAdjust(button.buttonId, localSliderValue.toInt())
                            lastSentValue = localSliderValue.toInt()
                        },
                        valueRange = 0f..100f,
                        steps = 99
                    )
                }
            },
            confirmButton = {
                TextButton(onClick = { activeDialButton = null }) {
                    Text("Close")
                }
            }
        )
    }
    }  // end Column inside Scaffold
}  // end DeckGridScreen

@Composable
private fun DeckButton(
    button: ButtonModel,
    isEditMode: Boolean,
    onTap: () -> Unit,
    modifier: Modifier = Modifier,
    levelValue: Int? = null
) {
    Surface(
        shape = RoundedCornerShape(12.dp),
        color = if (isEditMode) MaterialTheme.colorScheme.secondaryContainer else MaterialTheme.colorScheme.primaryContainer,
        modifier = modifier
            .aspectRatio(1f)
            .clickable { onTap() }
            .then(
                if (isEditMode) Modifier.border(2.dp, MaterialTheme.colorScheme.primary, RoundedCornerShape(12.dp))
                else Modifier
            )
    ) {
        Box(contentAlignment = Alignment.Center, modifier = Modifier.fillMaxSize()) {
            Text(
                text = button.label,
                textAlign = TextAlign.Center,
                style = MaterialTheme.typography.bodyMedium,
                modifier = Modifier.padding(8.dp)
            )
        }
    }
}

@Composable
private fun EmptyEditButton(onClick: () -> Unit) {
    Surface(
        shape = RoundedCornerShape(12.dp),
        color = Color.Transparent,
        modifier = Modifier
            .aspectRatio(1f)
            .border(1.dp, MaterialTheme.colorScheme.outline.copy(alpha = 0.5f), RoundedCornerShape(12.dp))
            .clickable { onClick() }
    ) {
        Box(contentAlignment = Alignment.Center, modifier = Modifier.fillMaxSize()) {
            Text(
                text = "+",
                style = MaterialTheme.typography.headlineMedium,
                color = MaterialTheme.colorScheme.outline
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun EditButtonDialog(
    button: ButtonModel,
    onDismiss: () -> Unit,
    onSave: (ButtonModel) -> Unit,
    onDelete: (() -> Unit)?
) {
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
    
    var dropdownExpanded by remember { mutableStateOf(false) }
    var mediaDropdownExpanded by remember { mutableStateOf(false) }
    var dialDropdownExpanded by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(if (onDelete == null) "Create Button" else "Edit Button") },
        text = {
            Column {
                OutlinedTextField(
                    value = label,
                    onValueChange = { label = it },
                    label = { Text("Label") },
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(modifier = Modifier.height(12.dp))

                ExposedDropdownMenuBox(
                    expanded = dropdownExpanded,
                    onExpandedChange = { dropdownExpanded = it }
                ) {
                    OutlinedTextField(
                        value = actionType,
                        onValueChange = {},
                        readOnly = true,
                        label = { Text("Action Type") },
                        trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = dropdownExpanded) },
                        modifier = Modifier
                            .menuAnchor()
                            .fillMaxWidth()
                    )
                    ExposedDropdownMenu(
                        expanded = dropdownExpanded,
                        onDismissRequest = { dropdownExpanded = false }
                    ) {
                        listOf("hotkey", "launch_app", "media_control", "open_url", "run_command", "text_snippet", "open_folder", "multi_action", "dial").forEach { type ->
                            DropdownMenuItem(
                                text = { Text(type) },
                                onClick = {
                                    actionType = type
                                    dropdownExpanded = false
                                }
                            )
                        }
                    }
                }
                Spacer(modifier = Modifier.height(12.dp))

                when (actionType) {
                    "hotkey" -> {
                        OutlinedTextField(
                            value = hotkeys,
                            onValueChange = { hotkeys = it },
                            label = { Text("Hotkey (e.g. Ctrl,Alt,A)") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "launch_app" -> {
                        OutlinedTextField(
                            value = path,
                            onValueChange = { path = it },
                            label = { Text("Path (e.g. notepad.exe)") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "media_control" -> {
                        ExposedDropdownMenuBox(
                            expanded = mediaDropdownExpanded,
                            onExpandedChange = { mediaDropdownExpanded = it }
                        ) {
                            OutlinedTextField(
                                value = mediaCommand,
                                onValueChange = {},
                                readOnly = true,
                                label = { Text("Media Command") },
                                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = mediaDropdownExpanded) },
                                modifier = Modifier
                                    .menuAnchor()
                                    .fillMaxWidth()
                            )
                            ExposedDropdownMenu(
                                expanded = mediaDropdownExpanded,
                                onDismissRequest = { mediaDropdownExpanded = false }
                            ) {
                                listOf("PlayPause", "NextTrack", "PrevTrack", "VolumeUp", "VolumeDown", "VolumeMute").forEach { cmd ->
                                    DropdownMenuItem(
                                        text = { Text(cmd) },
                                        onClick = {
                                            mediaCommand = cmd
                                            mediaDropdownExpanded = false
                                        }
                                    )
                                }
                            }
                        }
                    }
                    "open_url" -> {
                        OutlinedTextField(
                            value = url,
                            onValueChange = { url = it },
                            label = { Text("URL (e.g. https://google.com)") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "run_command" -> {
                        OutlinedTextField(
                            value = command,
                            onValueChange = { command = it },
                            label = { Text("Command (e.g. echo hello)") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "text_snippet" -> {
                        OutlinedTextField(
                            value = textValue,
                            onValueChange = { textValue = it },
                            label = { Text("Text to Paste") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "open_folder" -> {
                        OutlinedTextField(
                            value = targetFolderId,
                            onValueChange = { targetFolderId = it },
                            label = { Text("Target Folder ID (e.g. f_media)") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "multi_action" -> {
                        OutlinedTextField(
                            value = multiActionText,
                            onValueChange = { multiActionText = it },
                            label = { Text("Sequence (e.g. hotkey: Ctrl,C)") },
                            modifier = Modifier.fillMaxWidth(),
                            minLines = 3,
                            maxLines = 5
                        )
                    }
                    "dial" -> {
                        ExposedDropdownMenuBox(
                            expanded = dialDropdownExpanded,
                            onExpandedChange = { dialDropdownExpanded = it }
                        ) {
                            OutlinedTextField(
                                value = dialTarget,
                                onValueChange = {},
                                readOnly = true,
                                label = { Text("Dial Target") },
                                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = dialDropdownExpanded) },
                                modifier = Modifier
                                    .menuAnchor()
                                    .fillMaxWidth()
                            )
                            ExposedDropdownMenu(
                                expanded = dialDropdownExpanded,
                                onDismissRequest = { dialDropdownExpanded = false }
                            ) {
                                listOf("volume", "brightness").forEach { target ->
                                    DropdownMenuItem(
                                        text = { Text(target) },
                                        onClick = {
                                            dialTarget = target
                                            dialDropdownExpanded = false
                                        }
                                    )
                                }
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (label.isBlank()) return@Button
                    val action = when (actionType) {
                        "hotkey" -> ActionModel(type = "hotkey", keys = hotkeys.split(",").map { it.trim() }.filter { it.isNotEmpty() })
                        "launch_app" -> ActionModel(type = "launch_app", path = path.trim())
                        "media_control" -> ActionModel(type = "media_control", mediaCommand = mediaCommand)
                        "open_url" -> ActionModel(type = "open_url", url = url.trim())
                        "run_command" -> ActionModel(type = "run_command", command = command.trim())
                        "text_snippet" -> ActionModel(type = "text_snippet", text = textValue)
                        "open_folder" -> ActionModel(type = "open_folder", targetFolderId = targetFolderId.trim())
                        "multi_action" -> {
                            val parsed = parseMultiAction(multiActionText)
                            ActionModel(type = "multi_action", actions = parsed.first, delays = parsed.second)
                        }
                        "dial" -> ActionModel(type = "dial", dialTarget = dialTarget)
                        else -> ActionModel(type = "hotkey")
                    }
                    onSave(button.copy(label = label, action = action))
                }
            ) {
                Text("Save")
            }
        },
        dismissButton = {
            Row {
                if (onDelete != null) {
                    Button(
                        onClick = onDelete,
                        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error)
                    ) {
                        Text("Delete")
                    }
                    Spacer(modifier = Modifier.width(8.dp))
                }
                TextButton(onClick = onDismiss) {
                    Text("Cancel")
                }
            }
        }
    )
}

private fun formatMultiAction(actions: List<ActionModel>?, delays: List<Int>?): String {
    if (actions == null) return ""
    val sb = java.lang.StringBuilder()
    for (i in actions.indices) {
        val act = actions[i]
        when (act.type) {
            "hotkey" -> sb.append("hotkey: ").append(act.keys?.joinToString(",") ?: "").append("\n")
            "launch_app" -> sb.append("launch_app: ").append(act.path ?: "").append("\n")
            "media_control" -> sb.append("media_control: ").append(act.mediaCommand ?: "").append("\n")
            "open_url" -> sb.append("open_url: ").append(act.url ?: "").append("\n")
            "run_command" -> sb.append("run_command: ").append(act.command ?: "").append("\n")
            "text_snippet" -> sb.append("text_snippet: ").append(act.text ?: "").append("\n")
        }
        val delay = delays?.getOrNull(i) ?: 0
        if (delay > 0) {
            sb.append("delay: ").append(delay).append("\n")
        }
    }
    return sb.toString()
}

private fun parseMultiAction(text: String): Pair<List<ActionModel>, List<Int>> {
    val actions = mutableListOf<ActionModel>()
    val delays = mutableListOf<Int>()
    val lines = text.split("\n")
    for (line in lines) {
        val trimmed = line.trim()
        if (trimmed.isEmpty()) continue
        val colonIdx = trimmed.indexOf(':')
        if (colonIdx <= 0) continue
        val key = trimmed.substring(0, colonIdx).trim().lowercase()
        val valStr = trimmed.substring(colonIdx + 1).trim()
        if (key == "delay") {
            val ms = valStr.toIntOrNull() ?: 0
            if (actions.isNotEmpty() && ms >= 0) {
                while (delays.size < actions.size) {
                    delays.add(0)
                }
                delays[actions.size - 1] = ms
            }
        } else {
            val sub = when (key) {
                "hotkey" -> ActionModel(type = "hotkey", keys = valStr.split(",").map { it.trim() }.filter { it.isNotEmpty() })
                "launch_app" -> ActionModel(type = "launch_app", path = valStr)
                "media_control" -> ActionModel(type = "media_control", mediaCommand = valStr)
                "open_url" -> ActionModel(type = "open_url", url = valStr)
                "run_command" -> ActionModel(type = "run_command", command = valStr)
                "text_snippet" -> ActionModel(type = "text_snippet", text = valStr)
                else -> null
            }
            if (sub != null) {
                actions.add(sub)
            }
        }
    }
    while (delays.size < actions.size) {
        delays.add(0)
    }
    return Pair(actions, delays)
}
