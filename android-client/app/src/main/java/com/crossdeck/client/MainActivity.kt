package com.crossdeck.client

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.Color
import com.crossdeck.client.connection.ConnectionManager
import com.crossdeck.client.connection.ConnectionState
import com.crossdeck.client.ui.DeckGridScreen
import com.crossdeck.client.ui.PairingScreen
import com.crossdeck.client.ui.ReconnectOverlay
import com.crossdeck.client.ui.theme.CrossDeckTheme
import com.crossdeck.client.ui.theme.SignalCyan
import java.io.File

class MainActivity : ComponentActivity() {

    private lateinit var connectionManager: ConnectionManager

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        connectionManager = ConnectionManager(applicationContext)
        // If we've paired before, try to reconnect silently with the saved token.
        // If this returns false (no saved pairing), the PairingScreen shows immediately.
        connectionManager.reconnectWithSavedToken()

        setContent {
            val accentColorHex by connectionManager.accentColor.collectAsState()
            val parsedAccentColor = try {
                Color(android.graphics.Color.parseColor(accentColorHex))
            } catch (e: Exception) {
                SignalCyan
            }

            CrossDeckTheme(accentColor = parsedAccentColor) {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background
                ) {
                    val state by connectionManager.connectionState.collectAsState()
                    val profile by connectionManager.currentProfile.collectAsState()
                    val profiles by connectionManager.profilesList.collectAsState()
                    val activeProfileId by connectionManager.activeProfileId.collectAsState()
                    val error by connectionManager.lastError.collectAsState()
                    val toastMessage by connectionManager.toastMessage.collectAsState()
                    val dialLevels by connectionManager.dialLevels.collectAsState()
                    val activeButtons by connectionManager.activeButtons.collectAsState()
                    val runningApps by connectionManager.runningApps.collectAsState()
                    val connectedHostUrl by connectionManager.connectedHostUrl.collectAsState()
                    val appList by connectionManager.appList.collectAsState()
                    val extractedIcon by connectionManager.extractedIcon.collectAsState()
                    val isPcResponding by connectionManager.isPcResponding.collectAsState()
                    val reconnectGaveUp by connectionManager.reconnectGaveUp.collectAsState()

                    // "Manual Connection" in the reconnect overlay forces PairingScreen even though
                    // we still have a last-known profile; reset once a fresh connection succeeds.
                    var showManualPairing by remember { mutableStateOf(false) }
                    LaunchedEffect(state) {
                        if (state == ConnectionState.Connected) showManualPairing = false
                    }
                    // Don't leave the "Reconnecting…" spinner up forever once the 10s auto-retry
                    // window gives up — jump to manual pairing the same as tapping the button.
                    LaunchedEffect(reconnectGaveUp) {
                        if (reconnectGaveUp) {
                            connectionManager.disconnect()
                            showManualPairing = true
                        }
                    }

                    var appSettings by remember { mutableStateOf(connectionManager.loadSettings()) }
                    val savedIp = connectionManager.getLastSavedIp()
                    val savedPort = connectionManager.getLastSavedPort()
                    val hostInfo = if (savedIp.isNotBlank()) "$savedIp:$savedPort" else null

                    when {
                        state == ConnectionState.Connected && profile != null -> {
                            DeckGridScreen(
                                profile = profile!!,
                                profiles = profiles,
                                activeProfileId = activeProfileId,
                                connectedHostUrl = connectedHostUrl,
                                authToken = connectionManager.getToken(),
                                toastMessage = toastMessage,
                                dialLevels = dialLevels,
                                activeButtons = activeButtons,
                                isPcResponding = isPcResponding,
                                accentColorHex = accentColorHex,
                                onAccentColorChange = { color ->
                                    connectionManager.sendStyleChange(color)
                                },
                                onButtonTap = { button ->
                                    connectionManager.sendButtonPress(button.buttonId)
                                },
                                onButtonSave = { updatedButton ->
                                    connectionManager.sendProfileEditUpdate(profile!!.profileId, updatedButton)
                                },
                                onIconUpload = { bytes ->
                                    connectionManager.uploadIcon(bytes)
                                },
                                appList = appList,
                                onRequestAppList = { connectionManager.sendListAppsRequest() },
                                extractedIcon = extractedIcon,
                                onRequestExtractIcon = { path -> connectionManager.sendExtractIconRequest(path) },
                                onButtonDelete = { buttonId ->
                                    connectionManager.sendProfileEditDelete(profile!!.profileId, buttonId)
                                },
                                onProfileSwitch = { profileId ->
                                    connectionManager.sendProfileSwitch(profileId)
                                },
                                onProfileCreate = { name ->
                                    connectionManager.sendProfileCreate(name)
                                },
                                onProfileDelete = { profileId ->
                                    connectionManager.sendProfileDelete(profileId)
                                },
                                onProfileRename = { profileId, name ->
                                    connectionManager.sendProfileRename(profileId, name)
                                },
                                onDialAdjust = { buttonId, value ->
                                    connectionManager.sendDialAdjust(buttonId, value)
                                },
                                settings = appSettings,
                                onSettingsChange = { newSettings ->
                                    appSettings = newSettings
                                    connectionManager.saveSettings(newSettings)
                                },
                                connectionHostInfo = hostInfo,
                                onForgetHost = { connectionManager.forgetPairing() },
                                onClearIconCache = {
                                    File(applicationContext.cacheDir, "assets").listFiles()?.forEach { it.delete() }
                                },
                                runningApps = runningApps,
                                onRunningAppsSubscribe = { connectionManager.sendRunningAppsSubscribe(it) },
                                onWindowFocus = { connectionManager.sendWindowFocus(it) },
                                onWindowClose = { connectionManager.sendWindowClose(it) },
                                onButtonPress = { button, pressType -> connectionManager.sendButtonPress(button.buttonId, pressType) }
                            )
                        }
                        profile != null && !showManualPairing -> {
                            // Mid-session drop: show the last-known profile greyed out behind a
                            // frosted reconnect overlay instead of dumping straight to pairing.
                            Box(Modifier.fillMaxSize()) {
                                Box(Modifier.alpha(0.35f)) {
                                    DeckGridScreen(
                                        profile = profile!!,
                                        profiles = profiles,
                                        activeProfileId = activeProfileId,
                                        connectedHostUrl = null,
                                        authToken = null,
                                        toastMessage = null,
                                        dialLevels = dialLevels,
                                activeButtons = activeButtons,
                                        accentColorHex = accentColorHex,
                                        onAccentColorChange = {},
                                        onButtonTap = {},
                                        onButtonSave = {},
                                        onIconUpload = { null },
                                        appList = emptyList(),
                                        onRequestAppList = {},
                                        extractedIcon = null,
                                        onRequestExtractIcon = {},
                                        onButtonDelete = {},
                                        onProfileSwitch = {},
                                        onProfileCreate = {},
                                        onProfileDelete = {},
                                        onProfileRename = { _, _ -> },
                                        onDialAdjust = { _, _ -> },
                                        settings = appSettings,
                                        onSettingsChange = { newSettings ->
                                            appSettings = newSettings
                                            connectionManager.saveSettings(newSettings)
                                        },
                                        connectionHostInfo = hostInfo,
                                        onForgetHost = { connectionManager.forgetPairing() },
                                        onClearIconCache = {
                                            File(applicationContext.cacheDir, "assets").listFiles()?.forEach { it.delete() }
                                        }
                                    )
                                }
                                ReconnectOverlay(
                                    accentColor = MaterialTheme.colorScheme.primary,
                                    onManualConnect = {
                                        connectionManager.disconnect()
                                        showManualPairing = true
                                    }
                                )
                            }
                        }
                        state == ConnectionState.Connecting -> {
                            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                                CircularProgressIndicator()
                            }
                        }
                        else -> {
                            // Never paired, or user forced Manual Connection — fall back to pairing.
                            PairingScreen(
                                connecting = state == ConnectionState.Connecting,
                                errorMessage = error,
                                defaultIp = connectionManager.getLastSavedIp(),
                                defaultPort = connectionManager.getLastSavedPort().toString(),
                                defaultPin = connectionManager.getLastSavedPin(),
                                accentColorHex = accentColorHex,
                                onConnect = { ip, port, pin ->
                                    showManualPairing = false
                                    connectionManager.connectWithPin(ip, port, pin)
                                },
                                onScan = { callback ->
                                    connectionManager.startDiscoveryScan(callback)
                                }
                            )
                        }
                    }
                }
            }
        }
    }

    override fun onDestroy() {
        connectionManager.disconnect()
        super.onDestroy()
    }
}
