package com.crossdeck.client.connection

import android.content.Context
import android.os.Handler
import android.os.Looper
import com.crossdeck.client.model.Profile
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.decodeFromJsonElement
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import kotlinx.serialization.json.encodeToJsonElement
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener

enum class ConnectionState { Disconnected, Connecting, Connected, AuthFailed, Error }

/**
 * Owns the WebSocket connection to the Windows Host. See shared-schema/protocol.md for the
 * exact message formats this implements (Milestone 1 subset: auth, profile_sync, button_press,
 * ack).
 */
class ConnectionManager(context: Context) {

    private val client = OkHttpClient.Builder()
        // No pingInterval here — the server sends an application-level heartbeat every 25 s
        // which keeps NAT/WiFi alive without touching WebSocket ping/pong frames.
        // OkHttp's built-in ping races with application SendAsync calls on the server stream.
        .build()

    private val json = Json { ignoreUnknownKeys = true }
    private var webSocket: WebSocket? = null
    private val prefs = context.getSharedPreferences("appname_pairing", Context.MODE_PRIVATE)

    private val _connectionState = MutableStateFlow(ConnectionState.Disconnected)
    val connectionState: StateFlow<ConnectionState> = _connectionState.asStateFlow()

    private val _currentProfile = MutableStateFlow<Profile?>(null)
    val currentProfile: StateFlow<Profile?> = _currentProfile.asStateFlow()

    private val _activeProfileId = MutableStateFlow("p_default")
    val activeProfileId: StateFlow<String> = _activeProfileId.asStateFlow()

    private val _profilesList = MutableStateFlow<List<com.crossdeck.client.model.ProfileHeader>>(emptyList())
    val profilesList: StateFlow<List<com.crossdeck.client.model.ProfileHeader>> = _profilesList.asStateFlow()

    private val _lastError = MutableStateFlow<String?>(null)
    val lastError: StateFlow<String?> = _lastError.asStateFlow()

    /** Non-null for ~2 s when a toast should be shown. True = success (green), False = error (red). */
    private val _toastMessage = MutableStateFlow<Pair<String, Boolean>?>(null)
    val toastMessage: StateFlow<Pair<String, Boolean>?> = _toastMessage.asStateFlow()

    private val _dialLevels = MutableStateFlow<Map<String, Int>>(emptyMap())
    val dialLevels: StateFlow<Map<String, Int>> = _dialLevels.asStateFlow()

    private fun emitToast(message: String, success: Boolean) {
        _toastMessage.value = Pair(message, success)
        CoroutineScope(Dispatchers.IO).launch {
            kotlinx.coroutines.delay(2500)
            _toastMessage.value = null
        }
    }

    fun connectWithPin(ip: String, port: Int, pin: String) {
        savePairing(ip, port)
        prefs.edit().putString(KEY_PIN, pin).apply()
        openSocket(ip, port) { ws -> sendAuth(ws, pin = pin) }
    }

    /** Returns false if there's no saved pairing to reconnect to. */
    fun reconnectWithSavedToken(): Boolean {
        val ip = prefs.getString(KEY_IP, null) ?: return false
        val port = prefs.getInt(KEY_PORT, -1)
        val token = prefs.getString(KEY_TOKEN, null) ?: return false
        if (port <= 0) return false
        openSocket(ip, port) { ws -> sendAuth(ws, token = token) }
        return true
    }

    fun sendButtonPress(buttonId: String, pressType: String = "short") {
        val obj = buildJsonObject {
            put("type", "button_press")
            put("buttonId", buttonId)
            put("pressType", pressType)
        }
        webSocket?.send(obj.toString())
    }

    fun sendProfileEditUpdate(profileId: String, button: com.crossdeck.client.model.ButtonModel) {
        val obj = buildJsonObject {
            put("type", "profile_edit")
            put("profileId", profileId)
            put("op", "update_button")
            put("button", json.encodeToJsonElement(com.crossdeck.client.model.ButtonModel.serializer(), button))
        }
        webSocket?.send(obj.toString())
    }

    fun sendProfileEditDelete(profileId: String, buttonId: String) {
        val obj = buildJsonObject {
            put("type", "profile_edit")
            put("profileId", profileId)
            put("op", "delete_button")
            put("buttonId", buttonId)
        }
        webSocket?.send(obj.toString())
    }

    fun sendProfileSwitch(profileId: String) {
        val obj = buildJsonObject {
            put("type", "profile_switch")
            put("profileId", profileId)
        }
        webSocket?.send(obj.toString())
    }

    fun sendProfileCreate(name: String) {
        val obj = buildJsonObject {
            put("type", "profile_create")
            put("name", name)
        }
        webSocket?.send(obj.toString())
    }

    fun sendProfileDelete(profileId: String) {
        val obj = buildJsonObject {
            put("type", "profile_delete")
            put("profileId", profileId)
        }
        webSocket?.send(obj.toString())
    }

    fun sendProfileRename(profileId: String, name: String) {
        val obj = buildJsonObject {
            put("type", "profile_rename")
            put("profileId", profileId)
            put("name", name)
        }
        webSocket?.send(obj.toString())
    }

    fun disconnect() {
        webSocket?.close(1000, "user disconnect")
        webSocket = null
        _connectionState.value = ConnectionState.Disconnected
    }

    private fun openSocket(ip: String, port: Int, onOpenSendAuth: (WebSocket) -> Unit) {
        _connectionState.value = ConnectionState.Connecting
        val request = Request.Builder().url("ws://$ip:$port/ws").build()
        webSocket = client.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(ws: WebSocket, response: Response) = onOpenSendAuth(ws)

            override fun onMessage(ws: WebSocket, text: String) = handleMessage(text)

            override fun onFailure(ws: WebSocket, t: Throwable, response: Response?) {
                _connectionState.value = ConnectionState.Error
                _lastError.value = t.message
            }

            override fun onClosed(ws: WebSocket, code: Int, reason: String) {
                _connectionState.value = ConnectionState.Disconnected
            }
        })
    }

    private fun sendAuth(ws: WebSocket, pin: String? = null, token: String? = null) {
        val obj = buildJsonObject {
            put("type", "auth")
            pin?.let { put("pin", it) }
            token?.let { put("token", it) }
        }
        ws.send(obj.toString())
    }

    private fun handleMessage(text: String) {
        try {
            val obj = json.parseToJsonElement(text).jsonObject
            when (obj["type"]?.jsonPrimitive?.content) {
                "auth_ok" -> {
                    obj["token"]?.jsonPrimitive?.content?.let { saveToken(it) }
                    _connectionState.value = ConnectionState.Connected
                }
                "auth_failed" -> {
                    _connectionState.value = ConnectionState.AuthFailed
                    _lastError.value = obj["reason"]?.jsonPrimitive?.content
                }
                "profile_sync" -> {
                    obj["profile"]?.let { profileEl ->
                        val newProfile = json.decodeFromJsonElement<Profile>(profileEl)
                        val prev = _currentProfile.value
                        _currentProfile.value = newProfile
                        // Only show toast when the content actually changed (PC edited something)
                        if (prev != null && prev.buttons != newProfile.buttons) {
                            emitToast("\uD83D\uDFE2 Profile updated from PC", success = true)
                        }
                    }
                }
                "ack" -> {
                    val status = obj["status"]?.jsonPrimitive?.content
                    val message = obj["message"]?.jsonPrimitive?.content
                    if (status == "error") {
                        val displayMsg = message ?: "Unknown error"
                        _lastError.value = displayMsg
                        emitToast("\uD83D\uDD34 Error: $displayMsg", success = false)
                    }
                    // success acks are silent — the button tap itself is feedback enough
                }
                "heartbeat" -> { /* server keepalive — no action needed */ }
                "profile_list" -> {
                    obj["activeProfileId"]?.jsonPrimitive?.content?.let {
                        _activeProfileId.value = it
                    }
                    obj["profiles"]?.let {
                        _profilesList.value = json.decodeFromJsonElement(kotlinx.serialization.builtins.ListSerializer(com.crossdeck.client.model.ProfileHeader.serializer()), it)
                    }
                }
                "dial_state" -> {
                    val btnId = obj["buttonId"]?.jsonPrimitive?.content
                    val newVal = obj["value"]?.jsonPrimitive?.content?.toIntOrNull()
                    if (btnId != null && newVal != null) {
                        _dialLevels.value = _dialLevels.value + (btnId to newVal)
                    }
                }
            }
        } catch (e: Exception) {
            android.util.Log.e("ConnectionManager", "Error parsing message: $text", e)
        }
    }

    fun startDiscoveryScan(onDiscovered: (ip: String, port: Int, hostName: String) -> Unit) {
        CoroutineScope(Dispatchers.IO).launch {
            var socket: java.net.DatagramSocket? = null
            try {
                socket = java.net.DatagramSocket()
                socket.soTimeout = 1500
                socket.broadcast = true

                val messageBytes = "CROSSDECK_DISCOVER".toByteArray()
                val address = java.net.InetAddress.getByName("255.255.255.255")
                val packet = java.net.DatagramPacket(messageBytes, messageBytes.size, address, 7891)
                socket.send(packet)

                val buffer = ByteArray(1024)
                val responsePacket = java.net.DatagramPacket(buffer, buffer.size)
                socket.receive(responsePacket)

                val responseText = String(responsePacket.data, 0, responsePacket.length)
                val responseObj = json.parseToJsonElement(responseText).jsonObject
                val ip = responseObj["ip"]?.jsonPrimitive?.content ?: ""
                val port = responseObj["port"]?.jsonPrimitive?.content?.toIntOrNull() ?: 7890
                val hostName = responseObj["hostName"]?.jsonPrimitive?.content ?: ""

                if (ip.isNotBlank()) {
                    Handler(Looper.getMainLooper()).post { onDiscovered(ip, port, hostName) }
                }
            } catch (e: Exception) {
                // Ignore or log timeout
            } finally {
                socket?.close()
            }
        }
    }

    fun sendDialAdjust(buttonId: String, value: Int?) {
        val ws = webSocket ?: return
        val obj = buildJsonObject {
            put("type", "dial_adjust")
            put("buttonId", buttonId)
            if (value != null) {
                put("value", value)
            }
        }
        ws.send(obj.toString())
    }

    fun getLastSavedIp(): String = prefs.getString(KEY_IP, "") ?: ""
    fun getLastSavedPort(): Int = prefs.getInt(KEY_PORT, 7890)
    fun getLastSavedPin(): String = prefs.getString(KEY_PIN, "") ?: ""

    private fun savePairing(ip: String, port: Int) {
        prefs.edit().putString(KEY_IP, ip).putInt(KEY_PORT, port).apply()
    }

    private fun saveToken(token: String) {
        prefs.edit().putString(KEY_TOKEN, token).apply()
    }

    companion object {
        private const val KEY_IP = "ip"
        private const val KEY_PORT = "port"
        private const val KEY_PIN = "pin"
        private const val KEY_TOKEN = "token"
    }
}
