package com.crossdeck.client.connection

import android.content.Context
import android.os.Handler
import android.os.Looper
import com.crossdeck.client.model.AppSettings
import com.crossdeck.client.model.DiscoveredApp
import com.crossdeck.client.model.Profile
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.decodeFromJsonElement
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import kotlinx.serialization.json.encodeToJsonElement
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener

enum class ConnectionState { Disconnected, Connecting, Connected, AuthFailed, Error }

/**
 * Owns the WebSocket connection to the Windows Host and all message send/receive handling.
 */
class ConnectionManager(context: Context) {

    private val client = OkHttpClient.Builder()
        // No pingInterval here — the server sends an application-level heartbeat every 25 s
        // which keeps NAT/WiFi alive without touching WebSocket ping/pong frames.
        // OkHttp's built-in ping races with application SendAsync calls on the server stream.
        .build()

    private val json = Json { ignoreUnknownKeys = true }
    private var webSocket: WebSocket? = null
    private val prefs = context.getSharedPreferences("crossdeck_pairing", Context.MODE_PRIVATE)
    private val settingsPrefs = context.getSharedPreferences("crossdeck_settings", Context.MODE_PRIVATE)

    private val _connectionState = MutableStateFlow(ConnectionState.Disconnected)
    val connectionState: StateFlow<ConnectionState> = _connectionState.asStateFlow()

    private val _currentProfile = MutableStateFlow<Profile?>(null)
    val currentProfile: StateFlow<Profile?> = _currentProfile.asStateFlow()

    private val _activeProfileId = MutableStateFlow("p_default")
    val activeProfileId: StateFlow<String> = _activeProfileId.asStateFlow()

    // Matches ui/theme/Color.kt's SignalCyan and Windows' ThemeManager.DefaultAccentHex — don't let this drift.
    private val _accentColor = MutableStateFlow("#00E5FF")
    val accentColor: StateFlow<String> = _accentColor.asStateFlow()

    private val _profilesList = MutableStateFlow<List<com.crossdeck.client.model.ProfileHeader>>(emptyList())
    val profilesList: StateFlow<List<com.crossdeck.client.model.ProfileHeader>> = _profilesList.asStateFlow()

    private val _lastError = MutableStateFlow<String?>(null)
    val lastError: StateFlow<String?> = _lastError.asStateFlow()

    /** Non-null for ~2 s when a toast should be shown. True = success (green), False = error (red). */
    private val _toastMessage = MutableStateFlow<Pair<String, Boolean>?>(null)
    val toastMessage: StateFlow<Pair<String, Boolean>?> = _toastMessage.asStateFlow()

    private val _dialLevels = MutableStateFlow<Map<String, Int>>(emptyMap())
    val dialLevels: StateFlow<Map<String, Int>> = _dialLevels.asStateFlow()

    /** buttonId -> live "active" state (Mute actually muted, Play/Pause actually playing, launch_app actually focused). */
    private val _activeButtons = MutableStateFlow<Map<String, Boolean>>(emptyMap())
    val activeButtons: StateFlow<Map<String, Boolean>> = _activeButtons.asStateFlow()

    private val _connectedHostUrl = MutableStateFlow<String?>(null)
    val connectedHostUrl: StateFlow<String?> = _connectedHostUrl.asStateFlow()

    private val _appList = MutableStateFlow<List<DiscoveredApp>>(emptyList())
    val appList: StateFlow<List<DiscoveredApp>> = _appList.asStateFlow()

    private val _runningApps = MutableStateFlow<List<com.crossdeck.client.model.RunningApp>>(emptyList())
    val runningApps: StateFlow<List<com.crossdeck.client.model.RunningApp>> = _runningApps.asStateFlow()

    /** Pair(path, iconHashOrNull) — the most recent response to sendExtractIconRequest(). */
    private val _extractedIcon = MutableStateFlow<Pair<String, String?>?>(null)
    val extractedIcon: StateFlow<Pair<String, String?>?> = _extractedIcon.asStateFlow()

    private fun emitToast(message: String, success: Boolean) {
        _toastMessage.value = Pair(message, success)
        CoroutineScope(Dispatchers.IO).launch {
            kotlinx.coroutines.delay(2500)
            _toastMessage.value = null
        }
    }

    fun connectWithPin(ip: String, port: Int, pin: String) {
        cancelReconnect()
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
        cancelReconnect()
        webSocket?.close(1000, "user disconnect")
        webSocket = null
        _connectionState.value = ConnectionState.Disconnected
    }

    // ---- Reconnect backoff ----
    // Only kicks in after we've synced a profile at least once this run (_currentProfile != null)
    // — a bad PIN on first pairing should fall straight back to PairingScreen, not retry forever.

    private var reconnectJob: kotlinx.coroutines.Job? = null

    private fun scheduleReconnect() {
        if (reconnectJob?.isActive == true) return
        val ip = prefs.getString(KEY_IP, null) ?: return
        val port = prefs.getInt(KEY_PORT, -1)
        val token = prefs.getString(KEY_TOKEN, null) ?: return
        if (port <= 0) return

        reconnectJob = CoroutineScope(Dispatchers.IO).launch {
            var delayMs = 1000L
            val maxDelayMs = 30_000L
            while (true) {
                kotlinx.coroutines.delay(delayMs)
                if (_connectionState.value == ConnectionState.Connected) break
                openSocket(ip, port) { ws -> sendAuth(ws, token = token) }
                kotlinx.coroutines.delay(2000) // give the attempt a moment to resolve
                if (_connectionState.value == ConnectionState.Connected) break
                delayMs = (delayMs * 2).coerceAtMost(maxDelayMs)
            }
        }
    }

    private fun cancelReconnect() {
        reconnectJob?.cancel()
        reconnectJob = null
    }

    private fun openSocket(ip: String, port: Int, onOpenSendAuth: (WebSocket) -> Unit) {
        // Abandon any still-pending previous attempt first — otherwise a slow-to-fail connect
        // (e.g. a firewalled/dead IP) can leave two live sockets racing to set connectionState.
        webSocket?.cancel()
        _connectionState.value = ConnectionState.Connecting
        val request = Request.Builder().url("ws://$ip:$port/ws").build()
        webSocket = client.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(ws: WebSocket, response: Response) {
                if (ws !== webSocket) return // stale callback from a superseded attempt
                _connectedHostUrl.value = "http://$ip:${port + 1}/"
                onOpenSendAuth(ws)
            }

            override fun onMessage(ws: WebSocket, text: String) {
                if (ws !== webSocket) return
                handleMessage(text)
            }

            override fun onFailure(ws: WebSocket, t: Throwable, response: Response?) {
                if (ws !== webSocket) return
                _connectionState.value = ConnectionState.Error
                _lastError.value = t.message
                _connectedHostUrl.value = null
                _runningApps.value = emptyList()
                if (loadSettings().autoReconnect) scheduleReconnect()
            }

            override fun onClosed(ws: WebSocket, code: Int, reason: String) {
                if (ws !== webSocket) return
                _connectionState.value = ConnectionState.Disconnected
                _connectedHostUrl.value = null
                _runningApps.value = emptyList()
                if (loadSettings().autoReconnect) scheduleReconnect()
            }
        })
    }

    private fun sendAuth(ws: WebSocket, pin: String? = null, token: String? = null) {
        val obj = buildJsonObject {
            put("type", "auth")
            pin?.let { put("pin", it) }
            token?.let { put("token", it) }
            put("deviceName", android.os.Build.MODEL)
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
                    // Stop the backoff loop — a rejected token (e.g. host revoked this device)
                    // won't start working on the next retry. User falls back to the overlay's
                    // "Manual Connection" button (or PairingScreen if never connected).
                    cancelReconnect()
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
                    obj["accentColor"]?.jsonPrimitive?.content?.let {
                        _accentColor.value = it
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
                "button_state" -> {
                    val btnId = obj["buttonId"]?.jsonPrimitive?.content
                    if (btnId != null) {
                        obj["active"]?.let {
                            if (it !is kotlinx.serialization.json.JsonNull) _activeButtons.value = _activeButtons.value + (btnId to it.jsonPrimitive.boolean)
                        }
                        obj["level"]?.let {
                            if (it !is kotlinx.serialization.json.JsonNull) _dialLevels.value = _dialLevels.value + (btnId to it.jsonPrimitive.int)
                        }
                    }
                }
                "button_states" -> {
                    obj["states"]?.let { statesEl ->
                        var active = _activeButtons.value
                        var levels = _dialLevels.value
                        for (stateEl in statesEl.jsonArray) {
                            val btnId = stateEl.jsonObject["buttonId"]?.jsonPrimitive?.content ?: continue
                            stateEl.jsonObject["active"]?.let { if (it !is kotlinx.serialization.json.JsonNull) active = active + (btnId to it.jsonPrimitive.boolean) }
                            stateEl.jsonObject["level"]?.let { if (it !is kotlinx.serialization.json.JsonNull) levels = levels + (btnId to it.jsonPrimitive.int) }
                        }
                        _activeButtons.value = active
                        _dialLevels.value = levels
                    }
                }
                "running_apps" -> {
                    obj["apps"]?.let {
                        _runningApps.value = json.decodeFromJsonElement(
                            kotlinx.serialization.builtins.ListSerializer(com.crossdeck.client.model.RunningApp.serializer()), it
                        )
                    }
                }
                "app_list" -> {
                    obj["apps"]?.let {
                        _appList.value = json.decodeFromJsonElement(
                            kotlinx.serialization.builtins.ListSerializer(DiscoveredApp.serializer()), it
                        )
                    }
                }
                "icon_extracted" -> {
                    val path = obj["path"]?.jsonPrimitive?.content
                    val icon = obj["icon"]?.let { if (it is kotlinx.serialization.json.JsonNull) null else it.jsonPrimitive.content }
                    if (path != null) {
                        _extractedIcon.value = path to icon
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
                // Keep reading until soTimeout fires — a single receive() only ever sees the
                // first PC to reply and ignores every other host on the LAN.
                while (true) {
                    val responsePacket = java.net.DatagramPacket(buffer, buffer.size)
                    try {
                        socket.receive(responsePacket)
                    } catch (e: java.net.SocketTimeoutException) {
                        break // normal end of scan window, not an error
                    }

                    try {
                        val responseText = String(responsePacket.data, 0, responsePacket.length)
                        val responseObj = json.parseToJsonElement(responseText).jsonObject
                        val ip = responseObj["ip"]?.jsonPrimitive?.content ?: ""
                        val port = responseObj["port"]?.jsonPrimitive?.content?.toIntOrNull() ?: 7890
                        val hostName = responseObj["hostName"]?.jsonPrimitive?.content ?: ""

                        if (ip.isNotBlank()) {
                            Handler(Looper.getMainLooper()).post { onDiscovered(ip, port, hostName) }
                        }
                    } catch (e: Exception) {
                        android.util.Log.w("ConnectionManager", "Malformed discovery response, ignoring", e)
                    }
                }
            } catch (e: Exception) {
                android.util.Log.e("ConnectionManager", "LAN discovery scan failed", e)
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

    /** Requests the installed-apps list from the host — Android has no local way to enumerate
     * Windows Start Menu apps, unlike the PC editor's own AppDiscovery. Response arrives async via
     * the appList StateFlow. */
    fun sendListAppsRequest() {
        val ws = webSocket ?: return
        ws.send(buildJsonObject { put("type", "list_apps") }.toString())
    }

    fun sendRunningAppsSubscribe(subscribe: Boolean) {
        val ws = webSocket ?: return
        ws.send(buildJsonObject { put("type", if (subscribe) "running_apps_subscribe" else "running_apps_unsubscribe") }.toString())
        if (!subscribe) _runningApps.value = emptyList()
    }

    fun sendWindowFocus(hwnd: Long) {
        webSocket?.send(buildJsonObject { put("type", "window_focus"); put("hwnd", hwnd) }.toString())
    }

    fun sendWindowClose(hwnd: Long) {
        webSocket?.send(buildJsonObject { put("type", "window_close"); put("hwnd", hwnd) }.toString())
    }

    /** Asks the host to extract+save an icon for one specific exe path — called right after the
     * user picks an app from the list_apps dropdown, mirroring the PC editor's auto-icon-on-select.
     * Response arrives async via the extractedIcon StateFlow. */
    fun sendExtractIconRequest(path: String) {
        val ws = webSocket ?: return
        ws.send(buildJsonObject { put("type", "extract_icon"); put("path", path) }.toString())
    }

    fun sendStyleChange(colorHex: String) {
        val ws = webSocket ?: return
        val obj = buildJsonObject {
            put("type", "style_change")
            put("accentColor", colorHex)
        }
        ws.send(obj.toString())
    }

    fun getLastSavedIp(): String = prefs.getString(KEY_IP, "") ?: ""
    fun getLastSavedPort(): Int = prefs.getInt(KEY_PORT, 7890)
    fun getLastSavedPin(): String = prefs.getString(KEY_PIN, "") ?: ""
    fun getToken(): String? = prefs.getString(KEY_TOKEN, null)

    /** Clears the saved pairing (ip/port/pin/token) and disconnects — "Forget This PC" setting. */
    fun forgetPairing() {
        disconnect()
        prefs.edit().remove(KEY_IP).remove(KEY_PORT).remove(KEY_PIN).remove(KEY_TOKEN).apply()
    }

    fun loadSettings(): AppSettings = AppSettings(
        hapticsEnabled = settingsPrefs.getBoolean("haptics_enabled", true),
        compactGrid = settingsPrefs.getBoolean("compact_grid", false),
        keepScreenAwake = settingsPrefs.getBoolean("keep_screen_awake", false),
        iconOnlyMode = settingsPrefs.getBoolean("icon_only_mode", false),
        autoReconnect = settingsPrefs.getBoolean("auto_reconnect", true),
        confirmRunCommand = settingsPrefs.getBoolean("confirm_run_command", false)
    )

    fun saveSettings(settings: AppSettings) {
        settingsPrefs.edit()
            .putBoolean("haptics_enabled", settings.hapticsEnabled)
            .putBoolean("compact_grid", settings.compactGrid)
            .putBoolean("keep_screen_awake", settings.keepScreenAwake)
            .putBoolean("icon_only_mode", settings.iconOnlyMode)
            .putBoolean("auto_reconnect", settings.autoReconnect)
            .putBoolean("confirm_run_command", settings.confirmRunCommand)
            .apply()
    }

    /** Uploads raw image bytes to the host's asset endpoint; returns the hash filename to store
     * in ButtonModel.icon, or null on failure (not connected, no token, or a server/network error). */
    suspend fun uploadIcon(bytes: ByteArray): String? = withContext(Dispatchers.IO) {
        val hostUrl = _connectedHostUrl.value ?: return@withContext null
        val token = getToken() ?: return@withContext null
        try {
            val request = Request.Builder()
                .url("${hostUrl}assets/")
                .header("X-CrossDeck-Token", token)
                .post(bytes.toRequestBody("application/octet-stream".toMediaType()))
                .build()
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) return@withContext null
                val body = response.body?.string() ?: return@withContext null
                json.parseToJsonElement(body).jsonObject["icon"]?.jsonPrimitive?.content
            }
        } catch (e: Exception) {
            android.util.Log.e("ConnectionManager", "Icon upload failed", e)
            null
        }
    }

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
