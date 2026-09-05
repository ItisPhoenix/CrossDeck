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
import kotlinx.serialization.builtins.serializer
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.putJsonArray
import kotlinx.serialization.json.add
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
import java.security.SecureRandom
import java.util.concurrent.TimeUnit
import javax.net.ssl.HostnameVerifier
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLHandshakeException
import javax.net.ssl.SSLPeerUnverifiedException

enum class ConnectionState { Disconnected, Connecting, Connected, AuthFailed, Error }

/**
 * Owns the WebSocket connection to the Windows Host and all message send/receive handling.
 */
class ConnectionManager(context: Context) {

    private var client: OkHttpClient? = null

    private fun buildPinnedClient(fingerprint: String): OkHttpClient {
        val trustManager = PinnedCertificateTrustManager(fingerprint)
        val sslContext = SSLContext.getInstance("TLS").apply {
            init(null, arrayOf(trustManager), SecureRandom())
        }
        val hostVerifier: HostnameVerifier = HostnameVerifier { _, session ->
            // The host uses a self-signed certificate whose stable identity is the paired
            // fingerprint, not a DNS name. The trust manager still rejects every other cert.
            session.peerCertificates.firstOrNull()?.let(trustManager::matches) == true
        }
        return OkHttpClient.Builder()
            .sslSocketFactory(sslContext.socketFactory, trustManager)
            .hostnameVerifier(hostVerifier)
            .connectTimeout(10, TimeUnit.SECONDS)
            .writeTimeout(10, TimeUnit.SECONDS)
        // No pingInterval here — the server sends an application-level heartbeat every 25 s
        // which keeps NAT/WiFi alive without touching WebSocket ping/pong frames.
        // OkHttp's built-in ping races with application SendAsync calls on the server stream.
        //
        // readTimeout MUST be disabled: OkHttp's default is 10s and resets only on received
        // bytes. The server's 25s heartbeat is slower than that default, so any 10s+ gap in
        // traffic (i.e. whenever no button is being pressed) hit the read timeout and killed
        // the socket — this was the actual cause of "disconnects when idle/switching apps".
            .readTimeout(0, TimeUnit.MILLISECONDS)
            .build()
    }

    private val json = Json { ignoreUnknownKeys = true }
    private var activeSocket: WebSocket? = null
    private val pairingStore = SecurePairingStore(context)
    private val settingsPrefs = context.getSharedPreferences("crossdeck_settings", Context.MODE_PRIVATE)

    private val _hasSavedPairing = MutableStateFlow(hasStoredPairing())
    val hasSavedPairing: StateFlow<Boolean> = _hasSavedPairing.asStateFlow()

    private val _connectionState = MutableStateFlow(ConnectionState.Disconnected)
    val connectionState: StateFlow<ConnectionState> = _connectionState.asStateFlow()

    // Socket can stay open even if the PC hung without closing it — watch for heartbeat silence.
    private var lastMessageAtMs = android.os.SystemClock.elapsedRealtime()
    private var staleWatchdogJob: kotlinx.coroutines.Job? = null
    private val _isPcResponding = MutableStateFlow(true)
    val isPcResponding: StateFlow<Boolean> = _isPcResponding.asStateFlow()
    private val staleTimeoutMs = 45_000L

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

    private val _audioMixerApps = MutableStateFlow<List<com.crossdeck.client.model.AudioMixerApp>>(emptyList())
    val audioMixerApps: StateFlow<List<com.crossdeck.client.model.AudioMixerApp>> = _audioMixerApps.asStateFlow()

    private val _runningApps = MutableStateFlow<List<com.crossdeck.client.model.RunningApp>>(emptyList())
    val runningApps: StateFlow<List<com.crossdeck.client.model.RunningApp>> = _runningApps.asStateFlow()

    /** Triple(path, iconHashOrNull, nonce) — the nonce forces every response to count as a
     * distinct value, since StateFlow/Compose otherwise skip repeat-picked (path, hash) pairs. */
    private val _extractedIcon = MutableStateFlow<Triple<String, String?, Long>?>(null)
    val extractedIcon: StateFlow<Triple<String, String?, Long>?> = _extractedIcon.asStateFlow()

    private fun emitToast(message: String, success: Boolean) {
        _toastMessage.value = Pair(message, success)
        CoroutineScope(Dispatchers.IO).launch {
            kotlinx.coroutines.delay(1500)
            _toastMessage.value = null
        }
    }

    fun connectWithPin(ip: String, port: Int, pin: String, fingerprint: String) {
        val normalizedIp = PairingSecurity.normalizeIpv4(ip)
        val normalizedFingerprint = PairingSecurity.normalizeFingerprint(fingerprint)
        if (normalizedIp == null || !PairingSecurity.isValidPort(port) ||
            !PairingSecurity.isValidPin(pin) || normalizedFingerprint == null) {
            _lastError.value = "Enter a valid IPv4 address, port, PIN, and host fingerprint"
            return
        }
        cancelReconnect()
        pairingStore.remove(KEY_TOKEN)
        _hasSavedPairing.value = false
        _lastError.value = null
        savePairing(normalizedIp, port, normalizedFingerprint)
        openSocket(normalizedIp, port, normalizedFingerprint) { ws -> sendAuth(ws, pin = pin) }
    }

    /** Returns false if there's no saved pairing to reconnect to. Existing connected sessions are kept. */
    fun reconnectWithSavedToken(): Boolean {
        val ip = pairingStore.getString(KEY_IP) ?: return false
        val port = pairingStore.getString(KEY_PORT)?.toIntOrNull() ?: return false
        val token = pairingStore.getString(KEY_TOKEN) ?: return false
        val fingerprint = pairingStore.getString(KEY_FINGERPRINT) ?: return false
        if (!PairingSecurity.isValidPort(port)) return false
        _hasSavedPairing.value = true
        if (_connectionState.value == ConnectionState.Connected ||
            _connectionState.value == ConnectionState.Connecting) return true
        _lastError.value = null
        cancelReconnect()
        openSocket(ip, port, fingerprint) { ws -> sendAuth(ws, token = token) }
        return true
    }

    fun sendButtonPress(buttonId: String, pressType: String = "short", stepIndex: Int? = null) {
        val obj = buildJsonObject {
            put("type", "button_press")
            put("buttonId", buttonId)
            put("pressType", pressType)
            stepIndex?.let { put("stepIndex", it) }
        }
        activeSocket?.send(obj.toString())
    }

    /** list = "buttons" (default) or "dials" — same op shape, different target list on the host. */
    fun sendProfileEditUpdate(profileId: String, button: com.crossdeck.client.model.ButtonModel, list: String = "buttons") {
        val obj = buildJsonObject {
            put("type", "profile_edit")
            put("profileId", profileId)
            put("op", if (list == "dials") "update_dial" else "update_button")
            put("button", json.encodeToJsonElement(com.crossdeck.client.model.ButtonModel.serializer(), button))
        }
        activeSocket?.send(obj.toString())
    }

    fun sendProfileEditDelete(profileId: String, buttonId: String, list: String = "buttons") {
        val obj = buildJsonObject {
            put("type", "profile_edit")
            put("profileId", profileId)
            put("op", if (list == "dials") "delete_dial" else "delete_button")
            put("buttonId", buttonId)
        }
        activeSocket?.send(obj.toString())
    }

    fun sendProfileSwitch(profileId: String) {
        val obj = buildJsonObject {
            put("type", "profile_switch")
            put("profileId", profileId)
        }
        activeSocket?.send(obj.toString())
    }

    fun sendProfileCreate(name: String) {
        val obj = buildJsonObject {
            put("type", "profile_create")
            put("name", name)
        }
        activeSocket?.send(obj.toString())
    }

    fun sendProfileDelete(profileId: String) {
        val obj = buildJsonObject {
            put("type", "profile_delete")
            put("profileId", profileId)
        }
        activeSocket?.send(obj.toString())
    }

    fun sendProfileRename(profileId: String, name: String) {
        val obj = buildJsonObject {
            put("type", "profile_rename")
            put("profileId", profileId)
            put("name", name)
        }
        activeSocket?.send(obj.toString())
    }

    fun disconnect() {
        cancelReconnect()
        cancelConnectionWatchdog()
        activeSocket?.close(1000, "user disconnect")
        activeSocket = null
        _connectionState.value = ConnectionState.Disconnected
    }

    // ---- Reconnect backoff ----
    // A saved token is the pairing credential. Keep retrying while it exists; PairingScreen is
    // reserved for first-time pairing or an explicitly revoked/forgotten token.

    private var reconnectJob: kotlinx.coroutines.Job? = null
    private var connectionWatchdogJob: kotlinx.coroutines.Job? = null

    private fun scheduleReconnect() {
        if (reconnectJob?.isActive == true) return
        val ip = pairingStore.getString(KEY_IP) ?: return
        val port = pairingStore.getString(KEY_PORT)?.toIntOrNull() ?: return
        val token = pairingStore.getString(KEY_TOKEN) ?: return
        val fingerprint = pairingStore.getString(KEY_FINGERPRINT) ?: return
        if (!PairingSecurity.isValidPort(port)) return

        reconnectJob = CoroutineScope(Dispatchers.IO).launch {
            var delayMs = 1000L
            val maxDelayMs = 3_000L
            while (_hasSavedPairing.value && _connectionState.value != ConnectionState.Connected) {
                kotlinx.coroutines.delay(delayMs)
                if (!_hasSavedPairing.value || _connectionState.value == ConnectionState.Connected) break
                openSocket(ip, port, fingerprint) { ws -> sendAuth(ws, token = token) }
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

    private fun cancelConnectionWatchdog() {
        connectionWatchdogJob?.cancel()
        connectionWatchdogJob = null
    }

    private fun startConnectionWatchdog(webSocket: WebSocket, waitingForAuth: Boolean) {
        cancelConnectionWatchdog()
        val timeoutMs = if (waitingForAuth) 10_000L else 8_000L
        connectionWatchdogJob = CoroutineScope(Dispatchers.IO).launch {
            kotlinx.coroutines.delay(timeoutMs)
            if (webSocket !== activeSocket) return@launch
            val stillWaiting = if (waitingForAuth) {
                _connectionState.value == ConnectionState.Connecting
            } else {
                _connectionState.value == ConnectionState.Connected
            }
            if (!stillWaiting) return@launch

            android.util.Log.w(
                "ConnectionManager",
                if (waitingForAuth) "Timed out waiting for host authentication" else "Timed out waiting for initial profile sync"
            )
            _lastError.value = if (waitingForAuth) {
                "The PC did not finish connecting — retrying"
            } else {
                "The PC connected but did not send the deck — retrying"
            }
            webSocket.cancel()
            scheduleReconnect()
        }
    }

    private fun openSocket(ip: String, port: Int, fingerprint: String, onOpenSendAuth: (WebSocket) -> Unit) {
        // Abandon any still-pending previous attempt first — otherwise a slow-to-fail connect
        // (e.g. a firewalled/dead IP) can leave two live sockets racing to set connectionState.
        cancelConnectionWatchdog()
        activeSocket?.cancel()
        val pinnedClient = try {
            buildPinnedClient(fingerprint)
        } catch (e: IllegalArgumentException) {
            _connectionState.value = ConnectionState.AuthFailed
            _lastError.value = "The PC host fingerprint is invalid"
            return
        }
        client = pinnedClient
        PinnedClientRegistry.set(pinnedClient)
        _connectionState.value = ConnectionState.Connecting
        val request = Request.Builder().url("wss://$ip:$port/ws").build()
        activeSocket = pinnedClient.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                if (webSocket !== activeSocket) return // stale callback from a superseded attempt
                _connectedHostUrl.value = "https://$ip:${port + 1}/"
                onOpenSendAuth(webSocket)
                startConnectionWatchdog(webSocket, waitingForAuth = true)
                lastMessageAtMs = android.os.SystemClock.elapsedRealtime()
                _isPcResponding.value = true
                startStaleWatchdog()
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                if (webSocket !== activeSocket) return
                lastMessageAtMs = android.os.SystemClock.elapsedRealtime()
                _isPcResponding.value = true
                handleMessage(text)
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                if (webSocket !== activeSocket) return
                cancelConnectionWatchdog()
                staleWatchdogJob?.cancel()
                _connectionState.value = ConnectionState.Error
                android.util.Log.e("ConnectionManager", "Secure host connection failed", t)
                _lastError.value = when (t) {
                    is SSLHandshakeException,
                    is SSLPeerUnverifiedException -> "PC security check failed — rescan or verify the code"
                    else -> "Couldn’t reach the PC — check WiFi and host"
                }
                _connectedHostUrl.value = null
                _runningApps.value = emptyList()
                _audioMixerApps.value = emptyList()
                if (loadSettings().autoReconnect) scheduleReconnect()
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                if (webSocket !== activeSocket) return
                cancelConnectionWatchdog()
                staleWatchdogJob?.cancel()
                _connectionState.value = ConnectionState.Disconnected
                _connectedHostUrl.value = null
                _runningApps.value = emptyList()
                _audioMixerApps.value = emptyList()
                if (loadSettings().autoReconnect) scheduleReconnect()
            }
        })
    }

    /** Polls for heartbeat silence past staleTimeoutMs. */
    private fun startStaleWatchdog() {
        staleWatchdogJob?.cancel()
        staleWatchdogJob = CoroutineScope(Dispatchers.IO).launch {
            while (true) {
                kotlinx.coroutines.delay(10_000)
                val silentFor = android.os.SystemClock.elapsedRealtime() - lastMessageAtMs
                _isPcResponding.value = silentFor < staleTimeoutMs
            }
        }
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
                    val expectedFingerprint = pairingStore.getString(KEY_FINGERPRINT)
                        ?.let(PairingSecurity::normalizeFingerprint)
                    val returnedFingerprint = obj["fingerprint"]?.jsonPrimitive?.contentOrNull
                        ?.let(PairingSecurity::normalizeFingerprint)
                    if (expectedFingerprint == null || returnedFingerprint != expectedFingerprint) {
                        // Never accept a successful response from a host whose TLS identity was
                        // not the one confirmed during pairing. Clear the token so reconnect
                        // cannot keep sending it to a stale or replaced endpoint.
                        cancelReconnect()
                        clearSavedToken()
                        activeSocket?.cancel()
                        _connectionState.value = ConnectionState.AuthFailed
                        _lastError.value = "The PC host identity changed; pair it again"
                        return
                    }
                    val issuedToken = obj["token"]?.jsonPrimitive?.contentOrNull
                    if (issuedToken.isNullOrBlank()) {
                        cancelReconnect()
                        clearSavedToken()
                        activeSocket?.cancel()
                        _connectionState.value = ConnectionState.AuthFailed
                        _lastError.value = "The PC host returned an invalid authentication response"
                        return
                    }
                    saveToken(issuedToken)
                    // The PIN is only a first-pairing credential. Future connections use the token.
                    _connectionState.value = ConnectionState.Connected
                    activeSocket?.let { startConnectionWatchdog(it, waitingForAuth = false) }
                }
                "auth_failed" -> {
                    // A rejected saved token means the host revoked this pairing. Clear only the
                    // token, preserve the last host address, and allow a deliberate new pairing.
                    cancelConnectionWatchdog()
                    cancelReconnect()
                    clearSavedToken()
                    _connectionState.value = ConnectionState.AuthFailed
                    _lastError.value = obj["reason"]?.jsonPrimitive?.content
                }
                "profile_sync" -> {
                    obj["profile"]?.let { profileEl ->
                        val newProfile = json.decodeFromJsonElement<Profile>(profileEl)
                        val prev = _currentProfile.value
                        _currentProfile.value = newProfile
                        cancelConnectionWatchdog()
                        // Only show toast when the content actually changed (PC edited something)
                        if (prev != null && prev.buttons != newProfile.buttons) {
                            emitToast("Profile updated from PC", success = true)
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
                        emitToast("Error: $displayMsg", success = false)
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
                    val slot = obj["slot"]?.jsonPrimitive?.contentOrNull ?: "main"
                    if (btnId != null && newVal != null) {
                        _dialLevels.value = _dialLevels.value + ("$btnId:$slot" to newVal)
                    }
                }
                "button_state" -> {
                    val btnId = obj["buttonId"]?.jsonPrimitive?.content
                    if (btnId != null) {
                        obj["active"]?.let {
                            if (it !is kotlinx.serialization.json.JsonNull) _activeButtons.value = _activeButtons.value + (btnId to it.jsonPrimitive.boolean)
                        }
                        obj["level"]?.let {
                            if (it !is kotlinx.serialization.json.JsonNull) {
                                val slot = obj["slot"]?.jsonPrimitive?.contentOrNull ?: "main"
                                _dialLevels.value = _dialLevels.value + ("$btnId:$slot" to it.jsonPrimitive.int)
                            }
                        }
                    }
                }
                "button_states" -> {
                    obj["states"]?.let { statesEl ->
                        var active = _activeButtons.value
                        var levels = _dialLevels.value
                        for (stateEl in statesEl.jsonArray) {
                            val btnId = stateEl.jsonObject["buttonId"]?.jsonPrimitive?.content ?: continue
                            val slot = stateEl.jsonObject["slot"]?.jsonPrimitive?.contentOrNull ?: "main"
                            stateEl.jsonObject["active"]?.let { if (it !is kotlinx.serialization.json.JsonNull) active = active + (btnId to it.jsonPrimitive.boolean) }
                            stateEl.jsonObject["level"]?.let { if (it !is kotlinx.serialization.json.JsonNull) levels = levels + ("$btnId:$slot" to it.jsonPrimitive.int) }
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
                "audio_mixer" -> {
                    obj["apps"]?.let {
                        _audioMixerApps.value = json.decodeFromJsonElement(
                            kotlinx.serialization.builtins.ListSerializer(com.crossdeck.client.model.AudioMixerApp.serializer()), it
                        )
                    }
                }
                "icon_extracted" -> {
                    val path = obj["path"]?.jsonPrimitive?.content
                    val icon = obj["icon"]?.let { if (it is kotlinx.serialization.json.JsonNull) null else it.jsonPrimitive.content }
                    if (path != null) {
                        _extractedIcon.value = Triple(path, icon, System.nanoTime())
                    }
                }
            }
        } catch (e: Exception) {
            android.util.Log.e("ConnectionManager", "Error parsing message: $text", e)
        }
    }

    fun startDiscoveryScan(onDiscovered: (ip: String, port: Int, hostName: String, fingerprint: String) -> Unit) {
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
                        val version = responseObj["v"]?.jsonPrimitive?.int ?: 0
                        val tlsEnabled = responseObj["tls"]?.jsonPrimitive?.boolean ?: false
                        val ip = responseObj["ip"]?.jsonPrimitive?.content
                            ?.let(PairingSecurity::normalizeIpv4) ?: ""
                        val port = responseObj["port"]?.jsonPrimitive?.content?.toIntOrNull() ?: 7890
                        val hostName = responseObj["hostName"]?.jsonPrimitive?.content ?: ""
                        val fingerprint = responseObj["fingerprint"]?.jsonPrimitive?.content
                            ?.let(PairingSecurity::normalizeFingerprint)
                        val sourceIp = responsePacket.address?.hostAddress
                            ?.let(PairingSecurity::normalizeIpv4)

                        if (version == 2 && tlsEnabled && ip.isNotBlank() &&
                            PairingSecurity.isValidPort(port) && fingerprint != null && ip == sourceIp) {
                            Handler(Looper.getMainLooper()).post {
                                _lastError.value = null
                                onDiscovered(ip, port, hostName, fingerprint)
                            }
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

    fun sendDialAdjust(buttonId: String, slot: String, value: Int?) {
        val ws = activeSocket ?: return
        val obj = buildJsonObject {
            put("type", "dial_adjust")
            put("buttonId", buttonId)
            put("slot", slot)
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
        val ws = activeSocket ?: return
        ws.send(buildJsonObject { put("type", "list_apps") }.toString())
    }

    // Two independent screens (the dial-strip grid and the app-volume picker inside the dial
    // editor) can both want this feed live at once — a plain on/off toggle would have whichever
    // one unsubscribes first silently kill it for the other. Ref-counted so the wire message only
    // fires on the 0->1 and 1->0 transitions.
    private var audioMixerSubscriberCount = 0

    /** Subscribes to the live app-volume mixer — a push loop on the host sends an updated
     * audio_mixer message (one row per app currently playing audio) whenever anything changes,
     * same pattern as sendRunningAppsSubscribe. Response arrives async via audioMixerApps. */
    fun sendAudioMixerSubscribe(subscribe: Boolean) {
        if (subscribe) {
            audioMixerSubscriberCount++
            if (audioMixerSubscriberCount != 1) return
        } else {
            if (audioMixerSubscriberCount == 0) return
            audioMixerSubscriberCount--
            if (audioMixerSubscriberCount != 0) return
        }
        val ws = activeSocket ?: return
        ws.send(buildJsonObject { put("type", if (subscribe) "audio_mixer_subscribe" else "audio_mixer_unsubscribe") }.toString())
        if (!subscribe) _audioMixerApps.value = emptyList()
    }

    /** Adjusts one app's level and/or mute state in the live mixer; value/muted are independent,
     * omit whichever isn't changing. The updated row arrives back via the next audio_mixer push. */
    fun sendAudioMixerAdjust(processName: String, value: Int? = null, muted: Boolean? = null) {
        val ws = activeSocket ?: return
        ws.send(buildJsonObject {
            put("type", "audio_mixer_adjust")
            put("processName", processName)
            value?.let { put("value", it) }
            muted?.let { put("muted", it) }
        }.toString())
    }

    /** Sends a drag-reorder from the auto-flow grid — the host applies it to whichever folder
     * scope (root = null) these button IDs belong to, in the given order. */
    fun sendButtonsReorder(parentFolderId: String?, orderedButtonIds: List<String>, list: String = "buttons") {
        val ws = activeSocket ?: return
        ws.send(buildJsonObject {
            put("type", "buttons_reorder")
            put("parentFolderId", parentFolderId)
            put("list", list)
            putJsonArray("buttonIds") { orderedButtonIds.forEach { add(it) } }
        }.toString())
    }

    fun sendRunningAppsSubscribe(subscribe: Boolean) {
        val ws = activeSocket ?: return
        ws.send(buildJsonObject { put("type", if (subscribe) "running_apps_subscribe" else "running_apps_unsubscribe") }.toString())
        if (!subscribe) _runningApps.value = emptyList()
    }

    fun sendWindowFocus(hwnd: Long) {
        activeSocket?.send(buildJsonObject { put("type", "window_focus"); put("hwnd", hwnd) }.toString())
    }

    fun sendWindowClose(hwnd: Long) {
        activeSocket?.send(buildJsonObject { put("type", "window_close"); put("hwnd", hwnd) }.toString())
    }

    /** Asks the host to extract+save an icon for one specific exe path — called right after the
     * user picks an app from the list_apps dropdown, mirroring the PC editor's auto-icon-on-select.
     * Response arrives async via the extractedIcon StateFlow. */
    fun sendExtractIconRequest(path: String) {
        val ws = activeSocket ?: return
        ws.send(buildJsonObject { put("type", "extract_icon"); put("path", path) }.toString())
    }

    fun sendStyleChange(colorHex: String) {
        val ws = activeSocket ?: return
        val obj = buildJsonObject {
            put("type", "style_change")
            put("accentColor", colorHex)
        }
        ws.send(obj.toString())
    }

    fun getLastSavedIp(): String = pairingStore.getString(KEY_IP) ?: ""
    fun getLastSavedPort(): Int = pairingStore.getString(KEY_PORT)?.toIntOrNull() ?: 7890
    fun getLastSavedFingerprint(): String = pairingStore.getString(KEY_FINGERPRINT) ?: ""
    fun getToken(): String? = pairingStore.getString(KEY_TOKEN)

    /** Clears the saved pairing and disconnects — "Forget This PC" setting. */
    fun forgetPairing() {
        disconnect()
        pairingStore.clear()
        _hasSavedPairing.value = false
        _currentProfile.value = null
        _profilesList.value = emptyList()
    }

    fun loadSettings(): AppSettings = AppSettings(
        hapticsEnabled = settingsPrefs.getBoolean("haptics_enabled", true),
        compactGrid = settingsPrefs.getBoolean("compact_grid", false),
        keepScreenAwake = settingsPrefs.getBoolean("keep_screen_awake", false),
        iconOnlyMode = settingsPrefs.getBoolean("icon_only_mode", false),
        autoReconnect = settingsPrefs.getBoolean("auto_reconnect", true),
        confirmRunCommand = settingsPrefs.getBoolean("confirm_run_command", false),
        hasSeenEmptyCellHint = settingsPrefs.getBoolean("has_seen_empty_cell_hint", false),
        rotationLocked = settingsPrefs.getBoolean("rotation_locked", false)
    )

    fun saveSettings(settings: AppSettings) {
        settingsPrefs.edit()
            .putBoolean("haptics_enabled", settings.hapticsEnabled)
            .putBoolean("compact_grid", settings.compactGrid)
            .putBoolean("keep_screen_awake", settings.keepScreenAwake)
            .putBoolean("icon_only_mode", settings.iconOnlyMode)
            .putBoolean("auto_reconnect", settings.autoReconnect)
            .putBoolean("confirm_run_command", settings.confirmRunCommand)
            .putBoolean("has_seen_empty_cell_hint", settings.hasSeenEmptyCellHint)
            .putBoolean("rotation_locked", settings.rotationLocked)
            .apply()
    }

    /** Uploads raw image bytes to the host's asset endpoint; returns the hash filename to store
     * in ButtonModel.icon, or null on failure (not connected, no token, or a server/network error). */
    suspend fun uploadIcon(bytes: ByteArray): String? = withContext(Dispatchers.IO) {
        val hostUrl = _connectedHostUrl.value ?: return@withContext null
        val token = getToken() ?: return@withContext null
        val pinnedClient = client ?: return@withContext null
        try {
            val request = Request.Builder()
                .url("${hostUrl}assets/")
                .header("X-CrossDeck-Token", token)
                .post(bytes.toRequestBody("application/octet-stream".toMediaType()))
                .build()
            pinnedClient.newCall(request).execute().use { response ->
                if (!response.isSuccessful) return@withContext null
                val body = response.body?.string() ?: return@withContext null
                json.parseToJsonElement(body).jsonObject["icon"]?.jsonPrimitive?.content
            }
        } catch (e: Exception) {
            android.util.Log.e("ConnectionManager", "Icon upload failed", e)
            null
        }
    }

    private fun savePairing(ip: String, port: Int, fingerprint: String) {
        pairingStore.putString(KEY_IP, ip)
        pairingStore.putString(KEY_PORT, port.toString())
        pairingStore.putString(KEY_FINGERPRINT, fingerprint)
    }

    private fun saveToken(token: String) {
        pairingStore.putString(KEY_TOKEN, token)
        _hasSavedPairing.value = hasStoredPairing()
    }

    private fun clearSavedToken() {
        pairingStore.remove(KEY_TOKEN)
        _hasSavedPairing.value = false
    }

    private fun hasStoredPairing(): Boolean {
        val ip = pairingStore.getString(KEY_IP)
        val port = pairingStore.getString(KEY_PORT)?.toIntOrNull()
        val token = pairingStore.getString(KEY_TOKEN)
        val fingerprint = pairingStore.getString(KEY_FINGERPRINT)
        return !ip.isNullOrBlank() && port != null && PairingSecurity.isValidPort(port) &&
            !token.isNullOrBlank() && !fingerprint.isNullOrBlank()
    }

    companion object {
        private const val KEY_IP = "ip"
        private const val KEY_PORT = "port"
        private const val KEY_TOKEN = "token"
        private const val KEY_FINGERPRINT = "fingerprint"
    }
}
