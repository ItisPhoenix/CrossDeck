package com.crossdeck.client.ui

import androidx.compose.foundation.background
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
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
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
import android.view.HapticFeedbackConstants
import androidx.compose.ui.platform.LocalView
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
    connectedHostUrl: String?,
    authToken: String?,
    /** Pair(message, isSuccess) — non-null for ~2.5 s when a toast should show. */
    toastMessage: Pair<String, Boolean>?,
    dialLevels: Map<String, Int>,
    accentColorHex: String,
    onAccentColorChange: (String) -> Unit,
    onButtonTap: (ButtonModel) -> Unit,
    onButtonSave: (ButtonModel) -> Unit,
    onIconUpload: suspend (ByteArray) -> String?,
    onButtonDelete: (String) -> Unit,
    onProfileSwitch: (String) -> Unit,
    onProfileCreate: (String) -> Unit,
    onProfileDelete: (String) -> Unit,
    onProfileRename: (String, String) -> Unit,
    onDialAdjust: (String, Int?) -> Unit
) {
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val view = LocalView.current

    val accentColor = try {
        Color(android.graphics.Color.parseColor(accentColorHex))
    } catch (e: Exception) {
        Color(0xFF00F2FE)
    }

    // Dynamic scale oscillation for selector
    val infiniteTransition = rememberInfiniteTransition(label = "pulse")
    val pulseScale by infiniteTransition.animateFloat(
        initialValue = 0.98f,
        targetValue = 1.02f,
        animationSpec = androidx.compose.animation.core.infiniteRepeatable(
            animation = androidx.compose.animation.core.tween(1500, easing = androidx.compose.animation.core.LinearEasing),
            repeatMode = androidx.compose.animation.core.RepeatMode.Reverse
        ),
        label = "pulseScale"
    )

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

    var expanded by remember { mutableStateOf(false) }
    var showCreateDialog by remember { mutableStateOf(false) }
    var newProfileName by remember { mutableStateOf("") }
    var showRenameDialog by remember { mutableStateOf(false) }
    var renameProfileName by remember { mutableStateOf(profile.name) }
    var showSettingsPanel by remember { mutableStateOf(false) }

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
                    Text("Create", color = accentColor)
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
                    Text("Rename", color = accentColor)
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
                        Color(0xFF1B8040)
                    else
                        Color(0xFFC62828),
                    contentColor = Color.White
                )
            }
        }
    ) { innerPadding ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color(0xFF080810))
                .padding(innerPadding)
        ) {
            Column(modifier = Modifier.fillMaxSize()) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    // Profile selector dropdown
                    ExposedDropdownMenuBox(
                        expanded = expanded,
                        onExpandedChange = { expanded = !expanded }
                    ) {
                        OutlinedTextField(
                            readOnly = true,
                            value = profile.name,
                            onValueChange = {},
                            label = { Text("Profile", color = Color.Gray) },
                            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = expanded) },
                            colors = ExposedDropdownMenuDefaults.outlinedTextFieldColors(),
                            modifier = Modifier.scale(pulseScale).menuAnchor().width(180.dp)
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
                                    text = { Text("🗑 Delete Current", color = Color.Red) },
                                    onClick = {
                                        expanded = false
                                        onProfileDelete(activeProfileId)
                                    }
                                )
                            }
                        }
                    }

                    // Settings and edit triggers
                    Row {
                        Button(
                            onClick = {
                                view.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                                isEditMode = !isEditMode
                            },
                            colors = ButtonDefaults.buttonColors(
                                containerColor = if (isEditMode) accentColor else Color(0xFF0E0E10),
                                contentColor = if (isEditMode) Color.Black else Color.White
                            ),
                            modifier = Modifier.border(1.dp, Color(0xFF1F1F23), RoundedCornerShape(8.dp))
                        ) {
                            Text(if (isEditMode) "Done" else "Edit Grid")
                        }
                        Spacer(modifier = Modifier.width(8.dp))
                        Button(
                            onClick = {
                                view.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                                showSettingsPanel = !showSettingsPanel
                            },
                            colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF0E0E10), contentColor = Color.White),
                            modifier = Modifier.border(1.dp, Color(0xFF1F1F23), RoundedCornerShape(8.dp))
                        ) {
                            Text("⚙")
                        }
                    }
                }

                // Grid layout wrapper with 3D Flip capability
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .weight(1f)
                        .padding(16.dp)
                        .graphicsLayer {
                            rotationY = rotationYAngle
                            cameraDistance = 12f * density
                        }
                ) {
                    if (rotationYAngle <= 90f || rotationYAngle >= 270f) {
                        LazyVerticalGrid(
                            columns = GridCells.Fixed(cols),
                            contentPadding = PaddingValues(4.dp),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                            verticalArrangement = Arrangement.spacedBy(8.dp),
                            modifier = Modifier.fillMaxSize()
                        ) {
                            items(rows * cols) { index ->
                                val r = index / cols
                                val c = index % cols
                                val cellButton = buttonMap[r to c]

                                if (cellButton != null) {
                                    val dialValue = dialLevels[cellButton.buttonId]
                                    DeckButton(
                                        button = cellButton,
                                        isEditMode = isEditMode,
                                        connectedHostUrl = connectedHostUrl,
                                        authToken = authToken,
                                        accentColor = accentColor,
                                        onTap = {
                                            view.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
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
                                                } else {
                                                    onButtonTap(cellButton)
                                                }
                                            }
                                        },
                                        levelValue = dialValue
                                    )
                                } else {
                                    if (isEditMode) {
                                        EmptyEditButton(
                                            onClick = {
                                                view.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                                                creatingAtPosition = Position(r, c)
                                            }
                                        )
                                    } else {
                                        Box(
                                            modifier = Modifier
                                                .aspectRatio(1f)
                                                .border(1.dp, Color(0xFF1F1F23), RoundedCornerShape(12.dp))
                                                .background(Color(0xFF0E0E10), RoundedCornerShape(12.dp))
                                        )
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Themed Palette Switcher settings panel drawer
            if (showSettingsPanel) {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .background(Color.Black.copy(alpha = 0.5f))
                        .clickable { showSettingsPanel = false }
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .align(Alignment.BottomCenter)
                            .background(Color(0xFF0E0E10), RoundedCornerShape(topStart = 16.dp, topEnd = 16.dp))
                            .border(1.dp, Color(0xFF1F1F23), RoundedCornerShape(topStart = 16.dp, topEnd = 16.dp))
                            .padding(24.dp)
                            .clickable(enabled = false) {}
                    ) {
                        Text("Select Theme Accent", style = MaterialTheme.typography.titleMedium, color = Color.White)
                        Spacer(modifier = Modifier.height(16.dp))
                        listOf(
                            "Neon Cyan" to "#00d4ff",
                            "Neon Purple" to "#8b5cf6",
                            "Cyberpunk Yellow" to "#ffb703",
                            "Toxic Green" to "#2ec4b6",
                            "Crimson Red" to "#e63946"
                        ).forEach { (name, hex) ->
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .clickable {
                                        view.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                                        onAccentColorChange(hex)
                                        showSettingsPanel = false
                                    }
                                    .padding(vertical = 12.dp),
                                horizontalArrangement = Arrangement.SpaceBetween
                            ) {
                                Text(name, color = Color.White)
                                Box(
                                    modifier = Modifier
                                        .size(20.dp)
                                        .background(Color(android.graphics.Color.parseColor(hex)), RoundedCornerShape(10.dp))
                                )
                            }
                        }
                    }
                }
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
                        .background(Color.Black.copy(alpha = 0.6f))
                        .clickable { activeDialButton = null }
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .align(Alignment.BottomCenter)
                            .background(Color(0xFF0E0E10), RoundedCornerShape(topStart = 16.dp, topEnd = 16.dp))
                            .border(1.dp, Color(0xFF1F1F23), RoundedCornerShape(topStart = 16.dp, topEnd = 16.dp))
                            .padding(24.dp)
                            .clickable(enabled = false) {},
                        horizontalAlignment = Alignment.CenterHorizontally
                    ) {
                        val labelIcon = if (button.action.dialTarget == "brightness") "☀️" else "🔊"
                        Text(button.label, style = MaterialTheme.typography.titleMedium, color = Color.White)
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
                                    view.performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
                                    onDialAdjust(button.buttonId, newValue.toInt())
                                    lastSentValue = newValue.toInt()
                                }
                            },
                            onValueChangeFinished = {
                                isUserDragging = false
                                view.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
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
                            colors = ButtonDefaults.buttonColors(containerColor = accentColor, contentColor = Color.Black),
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
                    action = ActionModel(type = "hotkey")
                )
                EditButtonDialog(
                    button = newButtonPlaceholder,
                    connectedHostUrl = connectedHostUrl,
                    authToken = authToken,
                    onIconUpload = onIconUpload,
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

@Composable
private fun DeckButton(
    button: ButtonModel,
    isEditMode: Boolean,
    connectedHostUrl: String?,
    authToken: String?,
    accentColor: Color,
    onTap: () -> Unit,
    modifier: Modifier = Modifier,
    levelValue: Int? = null
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

    Surface(
        shape = RoundedCornerShape(12.dp),
        color = Color(0xFF0E0E10),
        modifier = modifier
            .scale(animatedScale)
            .aspectRatio(1f)
            .clickable(interactionSource = interactionSource, indication = null) { onTap() }
            .border(
                1.5.dp,
                if (isPressed) accentColor else if (isEditMode) accentColor.copy(alpha = 0.5f) else Color(0xFF1F1F23),
                RoundedCornerShape(12.dp)
            )
    ) {
        Box(contentAlignment = Alignment.Center, modifier = Modifier.fillMaxSize()) {
            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
                modifier = Modifier.padding(8.dp)
            ) {
                if (imageBitmap != null) {
                    Image(
                        bitmap = imageBitmap!!,
                        contentDescription = button.label,
                        modifier = Modifier.size(36.dp).padding(bottom = 4.dp)
                    )
                }
                Text(
                    text = button.label,
                    textAlign = TextAlign.Center,
                    style = MaterialTheme.typography.bodyMedium,
                    color = Color.White,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis
                )
                levelValue?.let {
                    Text(
                        text = "$it%",
                        textAlign = TextAlign.Center,
                        style = MaterialTheme.typography.bodySmall,
                        color = accentColor,
                        modifier = Modifier.padding(top = 2.dp)
                    )
                }
            }
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
            .border(1.5.dp, Color(0xFF1F1F23), RoundedCornerShape(12.dp))
            .clickable { onClick() }
    ) {
        Box(contentAlignment = Alignment.Center, modifier = Modifier.fillMaxSize()) {
            Text(
                text = "+",
                style = MaterialTheme.typography.headlineMedium,
                color = Color.Gray
            )
        }
    }
}

/**
 * Frosted overlay shown when a previously-connected session drops (Milestone 3b). Meant to be
 * stacked on top of a dimmed, touch-blocked DeckGridScreen showing the last-known profile —
 * see MainActivity's `profile != null && state != Connected` branch.
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
            .background(Color(0xCC080810))
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
            Text("Reconnecting…", color = Color.White, style = MaterialTheme.typography.bodyLarge)
            Spacer(modifier = Modifier.height(24.dp))
            TextButton(onClick = onManualConnect) {
                Text("Manual Connection", color = accentColor)
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun EditButtonDialog(
    button: ButtonModel,
    connectedHostUrl: String?,
    authToken: String?,
    onIconUpload: suspend (ByteArray) -> String?,
    onDismiss: () -> Unit,
    onSave: (ButtonModel) -> Unit,
    onDelete: (() -> Unit)?
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var iconValue by remember { mutableStateOf(button.icon) }
    var showBuiltinPicker by remember { mutableStateOf(false) }
    var isUploading by remember { mutableStateOf(false) }

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
    
    var dropdownExpanded by remember { mutableStateOf(false) }
    var mediaDropdownExpanded by remember { mutableStateOf(false) }
    var dialDropdownExpanded by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(if (onDelete == null) "Create Button" else "Edit Button") },
        text = {
            Column {
                TextField(
                    value = label,
                    onValueChange = { label = it },
                    label = { Text("Label") },
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(modifier = Modifier.height(12.dp))

                Text("Icon", style = MaterialTheme.typography.labelMedium)
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 4.dp)) {
                    IconPreview(icon = iconValue, connectedHostUrl = connectedHostUrl, authToken = authToken, modifier = Modifier.size(40.dp))
                    Spacer(modifier = Modifier.width(8.dp))
                    TextButton(onClick = { showBuiltinPicker = true }) { Text("Built-in") }
                    TextButton(onClick = { imagePickerLauncher.launch("image/*") }, enabled = !isUploading) {
                        Text(if (isUploading) "Uploading…" else "Upload")
                    }
                    if (iconValue != null) {
                        TextButton(onClick = { iconValue = null }) { Text("Clear") }
                    }
                }
                Spacer(modifier = Modifier.height(12.dp))

                ExposedDropdownMenuBox(
                    expanded = dropdownExpanded,
                    onExpandedChange = { dropdownExpanded = !dropdownExpanded }
                ) {
                    TextField(
                        readOnly = true,
                        value = actionType,
                        onValueChange = {},
                        label = { Text("Action Type") },
                        trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = dropdownExpanded) },
                        colors = ExposedDropdownMenuDefaults.outlinedTextFieldColors(),
                        modifier = Modifier.fillMaxWidth().menuAnchor()
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
                        TextField(
                            value = hotkeys,
                            onValueChange = { hotkeys = it },
                            label = { Text("Keys (comma-separated, e.g. Ctrl,Alt,A)") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "launch_app" -> {
                        TextField(
                            value = path,
                            onValueChange = { path = it },
                            label = { Text("Application Path") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "media_control" -> {
                        ExposedDropdownMenuBox(
                            expanded = mediaDropdownExpanded,
                            onExpandedChange = { mediaDropdownExpanded = !mediaDropdownExpanded }
                        ) {
                            TextField(
                                readOnly = true,
                                value = mediaCommand,
                                onValueChange = {},
                                label = { Text("Media Command") },
                                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = mediaDropdownExpanded) },
                                colors = ExposedDropdownMenuDefaults.outlinedTextFieldColors(),
                                modifier = Modifier.fillMaxWidth().menuAnchor()
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
                        TextField(
                            value = url,
                            onValueChange = { url = it },
                            label = { Text("URL") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "run_command" -> {
                        TextField(
                            value = command,
                            onValueChange = { command = it },
                            label = { Text("Command") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "text_snippet" -> {
                        TextField(
                            value = textValue,
                            onValueChange = { textValue = it },
                            label = { Text("Text Snippet") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "open_folder" -> {
                        TextField(
                            value = targetFolderId,
                            onValueChange = { targetFolderId = it },
                            label = { Text("Target Folder ID") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "multi_action" -> {
                        TextField(
                            value = multiActionText,
                            onValueChange = { multiActionText = it },
                            label = { Text("Actions (e.g. hotkey:Ctrl,C then delay:500)") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                    "dial" -> {
                        ExposedDropdownMenuBox(
                            expanded = dialDropdownExpanded,
                            onExpandedChange = { dialDropdownExpanded = !dialDropdownExpanded }
                        ) {
                            TextField(
                                readOnly = true,
                                value = dialTarget,
                                onValueChange = {},
                                label = { Text("Dial Target") },
                                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = dialDropdownExpanded) },
                                colors = ExposedDropdownMenuDefaults.outlinedTextFieldColors(),
                                modifier = Modifier.fillMaxWidth().menuAnchor()
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
                    onSave(button.copy(label = label.trim(), icon = iconValue, action = act))
                }
            ) {
                Text("Save")
            }
        },
        dismissButton = {
            if (onDelete != null) {
                TextButton(onClick = onDelete) {
                    Text("Delete", color = Color.Red)
                }
            } else {
                TextButton(onClick = onDismiss) {
                    Text("Cancel")
                }
            }
        }
    )

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
        modifier = modifier.background(Color(0xFF0E0E10), RoundedCornerShape(6.dp)),
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
        title = { Text("Choose Built-in Icon") },
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
                            .background(Color(0xFF0E0E10), RoundedCornerShape(6.dp))
                            .clickable { onSelect(name) },
                        contentAlignment = Alignment.Center
                    ) {
                        bitmap?.let { Image(bitmap = it, contentDescription = name, modifier = Modifier.size(28.dp)) }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        }
    )
}

private fun formatMultiAction(actions: List<ActionModel>?, delays: List<Int>?): String {
    if (actions == null) return ""
    val sb = StringBuilder()
    for (i in actions.indices) {
        val act = actions[i]
        when (act.type) {
            "hotkey" -> sb.append("hotkey:${act.keys?.joinToString(",") ?: ""}")
            "launch_app" -> sb.append("launch_app:${act.path ?: ""}")
            "media_control" -> sb.append("media_control:${act.mediaCommand ?: ""}")
            "open_url" -> sb.append("open_url:${act.url ?: ""}")
            "run_command" -> sb.append("run_command:${act.command ?: ""}")
            "text_snippet" -> sb.append("text_snippet:${act.text ?: ""}")
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
                val type = clean.substring(0, colonIdx).trim()
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
