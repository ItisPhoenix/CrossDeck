package com.crossdeck.client

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import com.crossdeck.client.connection.ConnectionManager
import com.crossdeck.client.connection.ConnectionState
import com.crossdeck.client.ui.DeckGridScreen
import com.crossdeck.client.ui.PairingScreen

class MainActivity : ComponentActivity() {

    private lateinit var connectionManager: ConnectionManager

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        connectionManager = ConnectionManager(applicationContext)
        // If we've paired before, try to reconnect silently with the saved token.
        // If this returns false (no saved pairing), the PairingScreen shows immediately.
        connectionManager.reconnectWithSavedToken()

        setContent {
            MaterialTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    val state by connectionManager.connectionState.collectAsState()
                    val profile by connectionManager.currentProfile.collectAsState()
                    val profiles by connectionManager.profilesList.collectAsState()
                    val activeProfileId by connectionManager.activeProfileId.collectAsState()
                    val error by connectionManager.lastError.collectAsState()
                    val toastMessage by connectionManager.toastMessage.collectAsState()
                    val dialLevels by connectionManager.dialLevels.collectAsState()

                    when {
                        state == ConnectionState.Connected && profile != null -> {
                            DeckGridScreen(
                                profile = profile!!,
                                profiles = profiles,
                                activeProfileId = activeProfileId,
                                toastMessage = toastMessage,
                                dialLevels = dialLevels,
                                onButtonTap = { button ->
                                    connectionManager.sendButtonPress(button.buttonId)
                                },
                                onButtonSave = { updatedButton ->
                                    connectionManager.sendProfileEditUpdate(profile!!.profileId, updatedButton)
                                },
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
                                }
                            )
                        }
                        state == ConnectionState.Connecting -> {
                            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                                CircularProgressIndicator()
                            }
                        }
                        else -> {
                            // Disconnected, AuthFailed, or Error — all fall back to manual pairing.
                            PairingScreen(
                                connecting = state == ConnectionState.Connecting,
                                errorMessage = error,
                                defaultIp = connectionManager.getLastSavedIp(),
                                defaultPort = connectionManager.getLastSavedPort().toString(),
                                defaultPin = connectionManager.getLastSavedPin(),
                                onConnect = { ip, port, pin ->
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
