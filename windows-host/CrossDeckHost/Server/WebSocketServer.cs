using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrossDeckHost.Actions;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost.Server;

/// <summary>
/// Deliberately built on raw TcpListener + a manual WebSocket handshake instead of
/// System.Net.HttpListener. HttpListener requires either Administrator privileges or a
/// `netsh http add urlacl` reservation for any prefix other than exactly "http://localhost/",
/// which is a dealbreaker for a consumer app users just double-click to run. TcpListener has
/// no such restriction, so we do the HTTP Upgrade handshake by hand and then wrap the raw
/// stream with WebSocket.CreateFromStream.
/// </summary>
public class WebSocketServer
{
    private const string WebSocketMagicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly int _port;
    private readonly PairingManager _pairing;
    private readonly ProfileStoreService _profileStore;
    private readonly ActionExecutor _actionExecutor;
    private readonly LiveStateService _liveState;
    private readonly List<WebSocket> _activeSockets = new();
    private readonly HashSet<WebSocket> _runningAppsSubs = new();
    private readonly HashSet<WebSocket> _runningAppsLoops = new(); // sockets with a push loop alive, so re-subscribe can't spawn a second
    private readonly HashSet<WebSocket> _audioMixerSubs = new();
    private readonly HashSet<WebSocket> _audioMixerLoops = new(); // sockets with a push loop alive, so re-subscribe can't spawn a second
    private readonly object _lock = new();

    private TcpListener? _listener;
    private TcpListener? _assetListener;
    private CancellationTokenSource? _cts;

    public int Port => _port;
    public string LocalIpAddress { get; private set; } = "unknown";
    public event Action? ClientAuthenticated;
    public event Action? ClientDisconnected;
    public string? ConnectedDeviceName { get; private set; }
    public bool IsClientConnected
    {
        get
        {
            lock (_lock)
            {
                return _activeSockets.Any(ws => ws.State == System.Net.WebSockets.WebSocketState.Open);
            }
        }
    }

    public WebSocketServer(int port, PairingManager pairing, ProfileStoreService profileStore, ActionExecutor actionExecutor, LiveStateService liveState)
    {
        _port = port;
        _pairing = pairing;
        _profileStore = profileStore;
        _actionExecutor = actionExecutor;
        _liveState = liveState;
        _liveState.StateChanged += (buttonId, active, level, dialSlot) => Task.Run(() => BroadcastButtonStateAsync(buttonId, active, level, dialSlot));
        LocalIpAddress = DetectLocalIpAddress();

        // Broadcast profile changes to all connected clients.
        // IMPORTANT: Use a single fused broadcast (profile_list + profile_sync together)
        // to avoid two concurrent Task.Run calls racing on the same WebSocket stream.
        _profileStore.ProfileChanged  += (_) => Task.Run(() => BroadcastAllAsync());
        _profileStore.ProfileSetChanged += (_) => Task.Run(() => BroadcastAllAsync());
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);

        // Asset (icon) server on the next port up. Same manual-TCP approach as the
        // WebSocket listener above — see class doc comment for why HttpListener is
        // avoided project-wide (admin/urlacl requirement).
        _assetListener = new TcpListener(IPAddress.Any, _port + 1);
        _assetListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _assetListener.Start();
        _ = AssetAcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _assetListener?.Stop();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken ct)
    {
        using var _ = tcpClient;
        var stream = tcpClient.GetStream();

        WebSocket webSocket;
        try
        {
            webSocket = await PerformHandshakeAsync(stream, ct);
        }
        catch
        {
            return; // Not a valid WS handshake request — drop the connection.
        }

        string? authedToken = null;
        CancellationTokenSource? heartbeatCts = null;
        string? lastMsgType = null;

        try
        {
            // First message must be auth (see shared-schema/protocol.md).
            var first = await ReceiveJsonAsync(webSocket, ct);
            if (first is null || GetType_(first.Value) != "auth")
            {
                await CloseAsync(webSocket, "expected auth message first");
                return;
            }

            authedToken = HandleAuth(first.Value, out var response);
            await SendJsonAsync(webSocket, response, ct);

            if (authedToken is null)
            {
                await CloseAsync(webSocket, "auth failed");
                return;
            }

            RegisterSocket(webSocket);
            ClientAuthenticated?.Invoke();

            // Auth succeeded — immediately push current profile state in one atomic send.
            await SendJsonAsync(webSocket, BuildProfileList(), ct);
            await SendJsonAsync(webSocket, BuildProfileSync(), ct);

            // Full live-state snapshot so buttons show correct state immediately instead of
            // waiting for the next change event (mute/media/focus/dial could be stale otherwise).
            // A failure here is a nice-to-have missing, not grounds for dropping an already
            // -authenticated connection — never let it take the socket down with it.
            try
            {
                var states = _liveState.GetSnapshot()
                    .Select(s => new { buttonId = s.ButtonId, active = s.Active, level = s.Level, slot = s.DialSlot })
                    .ToList();
                await SendJsonAsync(webSocket, new { type = "button_states", states }, ct);
            }
            catch { }

            // Application-level heartbeat: send a lightweight message every 25 s.
            // This keeps NAT/WiFi power-save from dropping the connection without
            // touching the WebSocket ping/pong protocol (which races with sends).
            heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeatTask = Task.Run(async () =>
            {
                try
                {
                    while (!heartbeatCts.Token.IsCancellationRequested &&
                           webSocket.State == WebSocketState.Open)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(25), heartbeatCts.Token);
                        if (webSocket.State == WebSocketState.Open)
                            await SendJsonAsync(webSocket, new { type = "heartbeat" }, CancellationToken.None);
                    }
                }
                catch { /* connection closed — normal */ }
            }, heartbeatCts.Token);

            // Main receive loop.
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var msg = await ReceiveJsonAsync(webSocket, ct);
                if (msg is null) break;

                lastMsgType = GetType_(msg.Value);
                switch (lastMsgType)
                {
                    case "button_press":
                        await HandleButtonPress(webSocket, msg.Value, ct);
                        break;
                    case "style_change":
                        var newColor = msg.Value.TryGetProperty("accentColor", out var colVal) ? colVal.GetString() : null;
                        if (newColor != null)
                        {
                            _profileStore.Set.AccentColor = newColor;
                            _profileStore.Save();
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                ThemeManager.AccentColor = newColor;
                                foreach (System.Windows.Window win in System.Windows.Application.Current.Windows)
                                {
                                    ThemeManager.ApplyTheme(win);
                                }
                            });
                            await BroadcastAllAsync();
                        }
                        break;
                    case "profile_edit":
                        await HandleProfileEdit(webSocket, msg.Value, ct);
                        break;
                    case "profile_switch":
                        var switchId = msg.Value.TryGetProperty("profileId", out var sId) ? sId.GetString() : null;
                        if (switchId != null) _profileStore.SwitchProfile(switchId);
                        break;
                    case "profile_create":
                        var createName = msg.Value.TryGetProperty("name", out var cName) ? cName.GetString() : null;
                        if (createName != null) _profileStore.CreateProfile(createName);
                        break;
                    case "profile_delete":
                        var deleteId = msg.Value.TryGetProperty("profileId", out var dId) ? dId.GetString() : null;
                        if (deleteId != null) _profileStore.DeleteProfile(deleteId);
                        break;
                    case "profile_rename":
                        var renameId = msg.Value.TryGetProperty("profileId", out var rId) ? rId.GetString() : null;
                        var renameName = msg.Value.TryGetProperty("name", out var rName) ? rName.GetString() : null;
                        if (renameId != null && renameName != null) _profileStore.RenameProfile(renameId, renameName);
                        break;
                    case "dial_adjust":
                        await HandleDialAdjust(webSocket, msg.Value, ct);
                        break;
                    case "buttons_reorder":
                        HandleButtonsReorder(msg.Value);
                        break;
                    case "list_apps":
                        await HandleListApps(webSocket, ct);
                        break;
                    case "audio_mixer_subscribe":
                        bool startMixerLoop;
                        lock (_lock)
                        {
                            _audioMixerSubs.Add(webSocket);
                            startMixerLoop = _audioMixerLoops.Add(webSocket);
                        }
                        if (startMixerLoop)
                        {
                            var mixerPushLoop = Task.Run(() => AudioMixerPushLoopAsync(webSocket, ct), ct);
                        }
                        break;
                    case "audio_mixer_unsubscribe":
                        lock (_lock) { _audioMixerSubs.Remove(webSocket); }
                        break;
                    case "audio_mixer_adjust":
                        HandleAudioMixerAdjust(msg.Value);
                        break;
                    case "running_apps_subscribe":
                        bool startLoop;
                        lock (_lock)
                        {
                            _runningAppsSubs.Add(webSocket);
                            startLoop = _runningAppsLoops.Add(webSocket);
                        }
                        if (startLoop)
                        {
                            var pushLoop = Task.Run(() => RunningAppsPushLoopAsync(webSocket, ct), ct);
                        }
                        break;
                    case "running_apps_unsubscribe":
                        lock (_lock) { _runningAppsSubs.Remove(webSocket); }
                        break;
                    case "window_focus":
                        if (msg.Value.TryGetProperty("hwnd", out var fEl) && fEl.TryGetInt64(out var fHwnd))
                        {
                            var focusTask = Task.Run(() => ActionExecutor.FocusWindow(new IntPtr(fHwnd)));
                        }
                        break;
                    case "window_close":
                        if (msg.Value.TryGetProperty("hwnd", out var clEl) && clEl.TryGetInt64(out var clHwnd))
                            RunningApps.CloseWindow(clHwnd);
                        break;
                    case "extract_icon":
                        await HandleExtractIcon(webSocket, msg.Value, ct);
                        break;
                    default:
                        // Ignore unknown message types
                        break;
                }
            }
        }
        catch (Exception ex) when
            (ex is WebSocketException ||
             ex is IOException ||
             ex is OperationCanceledException ||
             ex is InvalidOperationException)
        {
            // temp debug logging
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "crossdeck_ws_errors.log"), $"[{DateTime.Now:O}] filtered lastMsgType={lastMsgType} {ex.GetType().Name}: {ex.Message}\n\n"); } catch { }
        }
        catch (Exception ex)
        {
            // temp debug logging
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "crossdeck_ws_errors.log"), $"[{DateTime.Now:O}] lastMsgType={lastMsgType} {ex}\n\n"); } catch { }
        }
        finally
        {
            heartbeatCts?.Cancel();
            heartbeatCts?.Dispose();
            UnregisterSocket(webSocket);
            // Remove the per-socket semaphore to prevent memory leak.
            _semaphores.TryRemove(webSocket, out var removedSem);
            removedSem?.Dispose();

            if (webSocket.State == WebSocketState.Open)
            {
                try { await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { /* best effort */ }
            }
        }
    }

    private string? HandleAuth(JsonElement authMsg, out object response)
    {
        string? deviceName = null;
        if (authMsg.TryGetProperty("deviceName", out var devEl))
        {
            deviceName = devEl.GetString();
        }

        if (authMsg.TryGetProperty("token", out var tokenEl))
        {
            var token = tokenEl.GetString() ?? "";
            if (_pairing.ValidateToken(token))
            {
                ConnectedDeviceName = deviceName ?? "Android Device";
                response = new { type = "auth_ok", token, hostName = Environment.MachineName };
                return token;
            }
            response = new { type = "auth_failed", reason = "invalid_token" };
            return null;
        }

        if (authMsg.TryGetProperty("pin", out var pinEl))
        {
            var pin = pinEl.GetString() ?? "";
            if (_pairing.ValidatePin(pin))
            {
                var newToken = _pairing.IssueToken();
                ConnectedDeviceName = deviceName ?? "Android Device";
                response = new { type = "auth_ok", token = newToken, hostName = Environment.MachineName };
                return newToken;
            }
            response = new { type = "auth_failed", reason = "invalid_pin" };
            return null;
        }

        response = new { type = "auth_failed", reason = "no_pin_or_token_provided" };
        return null;
    }

    private async Task HandleButtonPress(WebSocket webSocket, JsonElement msg, CancellationToken ct)
    {
        var buttonId = msg.TryGetProperty("buttonId", out var idEl) ? idEl.GetString() : null;
        var pressType = msg.TryGetProperty("pressType", out var ptEl) ? ptEl.GetString() : "short";
        int? stepIndex = msg.TryGetProperty("stepIndex", out var siEl) && siEl.TryGetInt32(out var si) ? si : null;
        var button = _profileStore.Current.Buttons.FirstOrDefault(b => b.ButtonId == buttonId);

        if (button is null)
        {
            await SendJsonAsync(webSocket, new { type = "ack", buttonId, status = "error", message = "unknown buttonId" }, ct);
            return;
        }

        var action = pressType == "long" && button.LongPressAction != null ? button.LongPressAction : button.Action;

        // A tap on one tile inside the multi-action popup runs just that sub-action, not the chain.
        if (stepIndex is int idx && action.Type == "multi_action" && action.Actions != null && idx >= 0 && idx < action.Actions.Count)
            action = action.Actions[idx];

        _ = Task.Run(async () =>
        {
            try
            {
                var (success, error) = await _actionExecutor.ExecuteAsync(action);
                await SendJsonAsync(webSocket, success
                    ? new { type = "ack", buttonId, status = "ok" }
                    : new { type = "ack", buttonId, status = "error", message = error }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                await SendJsonAsync(webSocket, new { type = "ack", buttonId, status = "error", message = ex.Message }, CancellationToken.None);
            }
        });
    }

    /// <summary>Applies a drag-reorder from the Android client's auto-flow grid — same
    /// ReorderButtons used by the Windows editor's own drag-and-drop.</summary>
    private void HandleButtonsReorder(JsonElement msg)
    {
        var parentFolderId = msg.TryGetProperty("parentFolderId", out var fEl) && fEl.ValueKind != JsonValueKind.Null
            ? fEl.GetString() : null;
        if (!msg.TryGetProperty("buttonIds", out var idsEl) || idsEl.ValueKind != JsonValueKind.Array) return;

        var orderedIds = idsEl.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => s != null)
            .Select(s => s!)
            .ToList();

        _profileStore.ReorderButtons(_profileStore.Set.ActiveProfileId, parentFolderId, orderedIds);
    }

    private async Task HandleDialAdjust(WebSocket webSocket, JsonElement msg, CancellationToken ct)
    {
        var buttonId = msg.TryGetProperty("buttonId", out var idEl) ? idEl.GetString() : null;
        var slot = msg.TryGetProperty("slot", out var slotEl) ? slotEl.GetString() : "main";
        int targetVal = 0;
        var hasValue = msg.TryGetProperty("value", out var valEl) && valEl.TryGetInt32(out targetVal);

        var button = _profileStore.Current.Buttons.FirstOrDefault(b => b.ButtonId == buttonId);
        var action = slot == "longPress" ? button?.LongPressAction : button?.Action;
        if (button is null || action is null || action.Type != "dial")
        {
            await SendJsonAsync(webSocket, new { type = "ack", buttonId, status = "error", message = "invalid dial action configuration" }, ct);
            return;
        }

        var dialTarget = action.DialTarget;
        var resolvedButtonId = button.ButtonId;
        var resolvedSlot = slot ?? "main";

        // app_volume no longer has a single-app target to adjust here — it opens the live
        // app_mixer_subscribe/audio_mixer_adjust flow on the client instead (see HandleAudioMixerAdjust).
        if (dialTarget == "app_volume") return;

        // DDC/CI brightness writes go over I2C and can take tens of ms, unlike volume's
        // sub-millisecond COM call — offloaded so a fast slider drag doesn't back up behind
        // slow brightness calls on this socket's single receive loop (same as HandleButtonPress).
        _ = Task.Run(async () =>
        {
            int newVal = dialTarget == "volume"
                ? (hasValue ? CrossDeckHost.Actions.DialController.SetVolume(targetVal) : CrossDeckHost.Actions.DialController.GetVolume())
                : (hasValue ? CrossDeckHost.Actions.DialController.SetBrightness(targetVal) : CrossDeckHost.Actions.DialController.GetBrightness());

            if (newVal < 0)
            {
                await SendJsonAsync(webSocket, new { type = "ack", buttonId = resolvedButtonId, status = "error", message = "This display doesn't support brightness control" }, CancellationToken.None);
                return;
            }

            await BroadcastDialStateAsync(resolvedButtonId, resolvedSlot, newVal);
        });
    }

    /// <summary>
    /// Mirrors the PC editor's "installed apps" dropdown (AppDiscovery.DiscoverApps) for the
    /// Android client, which has no local way to enumerate Windows Start Menu apps. Deliberately
    /// name+path only — no icon extraction here, same reasoning as AppPickerWindow's in-memory-only
    /// previews: don't do per-row disk work for a list the user hasn't picked from yet. See
    /// HandleExtractIcon for the on-demand icon fetch once an app is actually selected.
    /// </summary>
    private async Task HandleListApps(WebSocket webSocket, CancellationToken ct)
    {
        var apps = await Task.Run(() => AppDiscovery.DiscoverApps(), ct);
        var payload = new
        {
            type = "app_list",
            apps = apps.Select(a => new { name = a.Name, path = a.ExePath })
        };
        await SendJsonAsync(webSocket, payload, ct);
    }

    /// <summary>Applies one row's slider drag or mute toggle from the live app-volume mixer.
    /// Doesn't reply directly — the next AudioMixerPushLoopAsync tick (sub-second) picks up the
    /// new level/muted state and pushes it to every subscriber, same as how dial_adjust for
    /// volume/brightness is observed via LiveStateService's poll rather than an inline reply.</summary>
    private void HandleAudioMixerAdjust(JsonElement msg)
    {
        var processName = msg.TryGetProperty("processName", out var pEl) ? pEl.GetString() : null;
        if (string.IsNullOrEmpty(processName)) return;

        bool? setMuted = msg.TryGetProperty("muted", out var mEl) && (mEl.ValueKind == JsonValueKind.True || mEl.ValueKind == JsonValueKind.False)
            ? mEl.GetBoolean() : null;
        int? setValue = msg.TryGetProperty("value", out var vEl) && vEl.TryGetInt32(out var v) ? v : null;

        _ = Task.Run(() =>
        {
            if (setValue is int value) CrossDeckHost.Actions.DialController.SetAppVolume(processName, value);
            if (setMuted is bool muted) CrossDeckHost.Actions.DialController.SetAppMuted(processName, muted);
        });
    }

    /// <summary>
    /// On-demand icon extraction for a single exe path — called by Android right after the user
    /// picks an app from the list_apps dropdown, so only the one app actually used gets its icon
    /// resized/hashed/saved to disk (same lazy pattern as list_apps above).
    /// </summary>
    private async Task HandleExtractIcon(WebSocket webSocket, JsonElement msg, CancellationToken ct)
    {
        var path = msg.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
        string? icon = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                icon = await ProfileStoreService.FetchFaviconIconAsync(path);
            }
            else if (path.Contains(".") && !Path.IsPathRooted(path) && !path.EndsWith(".exe") && !path.EndsWith(".lnk"))
            {
                icon = await ProfileStoreService.FetchFaviconIconAsync("https://" + path);
            }
            else
            {
                icon = await Task.Run(() => ProfileStoreService.ExtractAndSaveIcon(path), ct);
            }
        }
        await SendJsonAsync(webSocket, new { type = "icon_extracted", path, icon }, ct);
    }

    private async Task BroadcastDialStateAsync(string buttonId, string slot, int value)
    {
        var payload = new { type = "dial_state", buttonId, slot, value };
        List<WebSocket> targets;
        lock (_lock)
        {
            _activeSockets.RemoveAll(ws => ws.State != WebSocketState.Open);
            targets = new List<WebSocket>(_activeSockets);
        }
        foreach (var ws in targets)
        {
            try
            {
                await SendJsonAsync(ws, payload, CancellationToken.None);
            }
            catch { }
        }
    }

    /// <summary>Delta push for live button state — active/level/dialSlot are independent, whichever doesn't apply is null.</summary>
    private async Task BroadcastButtonStateAsync(string buttonId, bool? active, int? level, string? dialSlot)
    {
        var payload = new { type = "button_state", buttonId, active, level, slot = dialSlot };
        List<WebSocket> targets;
        lock (_lock)
        {
            _activeSockets.RemoveAll(ws => ws.State != WebSocketState.Open);
            targets = new List<WebSocket>(_activeSockets);
        }
        foreach (var ws in targets)
        {
            try
            {
                await SendJsonAsync(ws, payload, CancellationToken.None);
            }
            catch { }
        }
    }

    /// <summary>Pushes the live window list every 1s while this socket stays subscribed; skips sends when nothing changed.</summary>
    private async Task RunningAppsPushLoopAsync(WebSocket webSocket, CancellationToken ct)
    {
        string lastKey = "";
        try
        {
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                bool subscribed;
                lock (_lock) { subscribed = _runningAppsSubs.Contains(webSocket); }
                if (!subscribed) return;

                var windows = await Task.Run(() => RunningApps.GetWindows(), ct);
                var key = string.Join("|", windows.Select(w => $"{w.Hwnd}:{w.Title}:{w.Icon}:{w.Focused}"));
                if (key != lastKey)
                {
                    lastKey = key;
                    var apps = windows.Select(w => new { hwnd = w.Hwnd, title = w.Title, processName = w.ProcessName, icon = w.Icon, focused = w.Focused });
                    await SendJsonAsync(webSocket, new { type = "running_apps", apps }, ct);
                }

                await Task.Delay(1000, ct);
            }
        }
        catch { }
        finally
        {
            lock (_lock)
            {
                _runningAppsSubs.Remove(webSocket);
                _runningAppsLoops.Remove(webSocket);
            }
        }
    }

    /// <summary>Pushes the live app-volume mixer snapshot every 500ms while this socket stays
    /// subscribed; skips sends when nothing changed. Faster tick than RunningAppsPushLoopAsync's
    /// 1s since a slider drag needs to feel responsive, not just "eventually catches up".</summary>
    private async Task AudioMixerPushLoopAsync(WebSocket webSocket, CancellationToken ct)
    {
        string lastKey = "";
        try
        {
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                bool subscribed;
                lock (_lock) { subscribed = _audioMixerSubs.Contains(webSocket); }
                if (!subscribed) return;

                var entries = await Task.Run(() => CrossDeckHost.Actions.DialController.GetAudioMixerSnapshot(), ct);
                var key = string.Join("|", entries.Select(a => $"{a.ProcessName}:{a.Level}:{a.Muted}:{a.Icon}"));
                if (key != lastKey)
                {
                    lastKey = key;
                    var apps = entries.Select(a => new { processName = a.ProcessName, level = a.Level, muted = a.Muted, icon = a.Icon });
                    await SendJsonAsync(webSocket, new { type = "audio_mixer", apps }, ct);
                }

                await Task.Delay(500, ct);
            }
        }
        catch { }
        finally
        {
            lock (_lock)
            {
                _audioMixerSubs.Remove(webSocket);
                _audioMixerLoops.Remove(webSocket);
            }
        }
    }

    private async Task HandleProfileEdit(WebSocket webSocket, JsonElement msg, CancellationToken ct)
    {
        var profileId = msg.TryGetProperty("profileId", out var pIdEl) ? pIdEl.GetString() : null;
        var op = msg.TryGetProperty("op", out var opEl) ? opEl.GetString() : null;

        if (profileId is null || op is null)
        {
            await SendJsonAsync(webSocket, new { type = "ack", status = "error", message = "missing profileId or op" }, ct);
            return;
        }

        if (op == "update_button")
        {
            if (msg.TryGetProperty("button", out var btnEl))
            {
                try
                {
                    var button = JsonSerializer.Deserialize<ButtonModel>(btnEl.GetRawText());
                    if (button is not null)
                    {
                        _profileStore.UpdateButton(profileId, button);
                        await SendJsonAsync(webSocket, new { type = "ack", status = "ok" }, ct);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    await SendJsonAsync(webSocket, new { type = "ack", status = "error", message = ex.Message }, ct);
                    return;
                }
            }
            await SendJsonAsync(webSocket, new { type = "ack", status = "error", message = "missing button payload" }, ct);
        }
        else if (op == "delete_button")
        {
            var buttonId = msg.TryGetProperty("buttonId", out var bIdEl) ? bIdEl.GetString() : null;
            if (buttonId is not null)
            {
                _profileStore.DeleteButton(profileId, buttonId);
                await SendJsonAsync(webSocket, new { type = "ack", status = "ok" }, ct);
                return;
            }
            await SendJsonAsync(webSocket, new { type = "ack", status = "error", message = "missing buttonId" }, ct);
        }
        else
        {
            await SendJsonAsync(webSocket, new { type = "ack", status = "error", message = $"unknown op '{op}'" }, ct);
        }
    }

    /// <summary>
    /// Force-closes every currently connected client (used by the tray "Revoke Device" action,
    /// alongside PairingManager.RevokeAllTokens). The client's onClosed handler is what drives it
    /// to the reconnect overlay / re-pairing flow — see ConnectionManager.kt.
    /// </summary>
    public void DisconnectAllClients()
    {
        List<WebSocket> targets;
        lock (_lock) targets = new List<WebSocket>(_activeSockets);

        foreach (var ws in targets)
        {
            _ = CloseAsync(ws, "revoked");
        }
    }

    private void RegisterSocket(WebSocket ws)
    {
        lock (_lock) _activeSockets.Add(ws);
    }

    private void UnregisterSocket(WebSocket ws)
    {
        bool wasConnected;
        lock (_lock)
        {
            wasConnected = _activeSockets.Contains(ws);
            _activeSockets.Remove(ws);
        }
        if (wasConnected)
        {
            ConnectedDeviceName = null;
            ClientDisconnected?.Invoke();
        }
    }

    /// <summary>
    /// Sends both profile_list and profile_sync to every connected client in one
    /// serialised pass per socket. Replaces the old separate BroadcastProfileSyncAsync /
    /// BroadcastProfileListAsync pair that could race on the same WebSocket stream.
    /// </summary>
    private async Task BroadcastAllAsync()
    {
        var listPayload = BuildProfileList();
        var syncPayload = BuildProfileSync();
        List<WebSocket> targets;
        lock (_lock)
        {
            _activeSockets.RemoveAll(ws => ws.State != WebSocketState.Open);
            targets = new List<WebSocket>(_activeSockets);
        }

        foreach (var ws in targets)
        {
            try
            {
                // Send list then sync through the SAME per-socket semaphore, back-to-back,
                // so they are never interleaved with each other or with a heartbeat.
                await SendJsonAsync(ws, listPayload, CancellationToken.None);
                await SendJsonAsync(ws, syncPayload, CancellationToken.None);
            }
            catch { /* socket gone — ignore */ }
        }
    }

    private object BuildProfileSync() => new { type = "profile_sync", profile = _profileStore.Current, accentColor = _profileStore.Set.AccentColor };

    private object BuildProfileList() =>
        new
        {
            type = "profile_list",
            activeProfileId = _profileStore.Set.ActiveProfileId,
            profiles = _profileStore.Set.Profiles.Select(p => new
            {
                profileId = p.ProfileId,
                name = p.Name,
                icons = p.Buttons.Where(b => !string.IsNullOrEmpty(b.Icon)).Select(b => b.Icon).Take(4).ToList()
            }).ToList()
        };

    private static string GetType_(JsonElement el) =>
        el.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";

    // ---- WebSocket framing helpers ----

    private static async Task<JsonElement?> ReceiveJsonAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            // Skip non-text frames (binary, ping surfaced by managed WS) —
            // writing them into the JSON parser causes JsonException —> RST.
            if (result.MessageType != WebSocketMessageType.Text) continue;
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        if (ms.Length == 0) return null;
        ms.Position = 0;
        try
        {
            using var doc = JsonDocument.Parse(ms);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Malformed JSON frame — ignore, stay connected.
            return null;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<WebSocket, SemaphoreSlim> _semaphores = new();

    private static async Task SendJsonAsync(WebSocket ws, object payload, CancellationToken ct)
    {
        var sem = _semaphores.GetOrAdd(ws, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    private static async Task CloseAsync(WebSocket ws, string reason)
    {
        try { await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, CancellationToken.None); }
        catch { /* best effort */ }
    }

    // ---- Manual HTTP Upgrade handshake (see class doc comment for why) ----

    private static async Task<WebSocket> PerformHandshakeAsync(NetworkStream stream, CancellationToken ct)
    {
        var requestText = await ReadHttpHeadersAsync(stream, ct);
        var key = ExtractHeader(requestText, "Sec-WebSocket-Key")
                  ?? throw new InvalidOperationException("missing Sec-WebSocket-Key");

        var acceptKey = Convert.ToBase64String(
            SHA1.HashData(Encoding.UTF8.GetBytes(key + WebSocketMagicGuid)));

        var responseText =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

        var responseBytes = Encoding.ASCII.GetBytes(responseText);
        await stream.WriteAsync(responseBytes, ct);

        // Disable keepAliveInterval — we use an application-level heartbeat instead.
        // The built-in interval sends WebSocket PING frames that race with our application
        // SendAsync calls on the same NetworkStream, causing OkHttp pong timeouts.
        return WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null,
            keepAliveInterval: Timeout.InfiniteTimeSpan);
    }

    private static async Task<string> ReadHttpHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new List<byte>();
        var single = new byte[1];
        // Read byte-by-byte until we see the blank line ending HTTP headers ("\r\n\r\n").
        // Fine for a handshake (small, one-time) — not used for the actual message loop.
        while (true)
        {
            int n = await stream.ReadAsync(single.AsMemory(0, 1), ct);
            if (n == 0) break;
            buffer.Add(single[0]);
            if (buffer.Count >= 4 &&
                buffer[^4] == '\r' && buffer[^3] == '\n' && buffer[^2] == '\r' && buffer[^1] == '\n')
                break;
        }
        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static string? ExtractHeader(string headers, string name)
    {
        foreach (var line in headers.Split("\r\n"))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            if (string.Equals(line[..idx].Trim(), name, StringComparison.OrdinalIgnoreCase))
                return line[(idx + 1)..].Trim();
        }
        return null;
    }

    private static string DetectLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            // Doesn't actually send anything — just asks the OS which local interface would be
            // used to reach an external address, which is a reliable way to find the "real" LAN IP.
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    // ---- Asset (icon) server: same manual-TCP-parsed-HTTP approach as the WS handshake ----

    private async Task AssetAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _assetListener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }

            _ = HandleAssetClientAsync(client, ct);
        }
    }

    private async Task HandleAssetClientAsync(TcpClient tcpClient, CancellationToken ct)
    {
        using var _ = tcpClient;
        var stream = tcpClient.GetStream();

        try
        {
            var headerText = await ReadHttpHeadersAsync(stream, ct);
            var requestLine = headerText.Split("\r\n", 2)[0].Split(' ');
            if (requestLine.Length < 2)
            {
                await WriteHttpResponseAsync(stream, 400, "Bad Request");
                return;
            }

            var method = requestLine[0];
            var (path, query) = SplitPathQuery(requestLine[1]);

            var token = ExtractHeader(headerText, "X-CrossDeck-Token") ?? ExtractQueryParam(query, "token");
            if (string.IsNullOrEmpty(token) || !_pairing.ValidateToken(token))
            {
                await WriteHttpResponseAsync(stream, 401, "Unauthorized");
                return;
            }

            if (!path.StartsWith("/assets/"))
            {
                await WriteHttpResponseAsync(stream, 404, "Not Found");
                return;
            }

            if (method == "GET")
            {
                var hash = path["/assets/".Length..].Trim('/');
                // Icon hashes are always a bare SHA256 hex string (see SaveIconFromBytes) — reject
                // anything else before it reaches Path.Combine, so "../" or extra path segments
                // can't escape the Assets folder.
                if (hash.Length == 0 || !hash.All(Uri.IsHexDigit))
                {
                    await WriteHttpResponseAsync(stream, 400, "Bad Request");
                    return;
                }
                var assetsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrossDeckHost", "Assets");
                var filePath = Path.Combine(assetsDir, hash + ".png");

                if (!File.Exists(filePath))
                {
                    await WriteHttpResponseAsync(stream, 404, "Not Found");
                    return;
                }

                var bytes = await File.ReadAllBytesAsync(filePath, ct);
                await WriteHttpResponseAsync(stream, 200, "OK", "image/png", bytes);
            }
            else if (method == "POST")
            {
                const int maxUploadBytes = 10 * 1024 * 1024; // 10 MB, plenty for a source image pre-resize
                var contentLengthStr = ExtractHeader(headerText, "Content-Length");
                if (!int.TryParse(contentLengthStr, out var contentLength) || contentLength <= 0 || contentLength > maxUploadBytes)
                {
                    await WriteHttpResponseAsync(stream, 400, "Bad Request");
                    return;
                }

                var body = new byte[contentLength];
                var read = 0;
                while (read < contentLength)
                {
                    var n = await stream.ReadAsync(body.AsMemory(read, contentLength - read), ct);
                    if (n == 0) break;
                    read += n;
                }
                if (read < contentLength)
                {
                    await WriteHttpResponseAsync(stream, 400, "Bad Request");
                    return;
                }

                string hash;
                try
                {
                    hash = ProfileStoreService.SaveIconFromBytes(body);
                }
                catch
                {
                    await WriteHttpResponseAsync(stream, 400, "Bad Request");
                    return;
                }

                var json = JsonSerializer.Serialize(new { icon = hash });
                await WriteHttpResponseAsync(stream, 200, "OK", "application/json", Encoding.UTF8.GetBytes(json));
            }
            else
            {
                await WriteHttpResponseAsync(stream, 405, "Method Not Allowed");
            }
        }
        catch
        {
            // Best effort — client disconnected mid-request or sent garbage. Nothing to clean up
            // beyond the `using` on tcpClient above.
        }
    }

    private static async Task WriteHttpResponseAsync(NetworkStream stream, int statusCode, string statusText, string? contentType = null, byte[]? body = null)
    {
        var header =
            $"HTTP/1.1 {statusCode} {statusText}\r\n" +
            "Connection: close\r\n" +
            (contentType != null ? $"Content-Type: {contentType}\r\n" : "") +
            $"Content-Length: {body?.Length ?? 0}\r\n\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        if (body != null) await stream.WriteAsync(body);
    }

    private static (string path, string query) SplitPathQuery(string rawPath)
    {
        var qIdx = rawPath.IndexOf('?');
        return qIdx < 0 ? (rawPath, "") : (rawPath[..qIdx], rawPath[(qIdx + 1)..]);
    }

    private static string? ExtractQueryParam(string query, string name)
    {
        foreach (var pair in query.Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == name) return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }
}
