using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Integrations.Obs;

public enum ObsConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed,
}

/// <summary>Cached OBS state so keys can show what is currently happening.</summary>
public sealed class ObsState
{
    public string? CurrentScene { get; internal set; }
    public bool Streaming { get; internal set; }
    public bool Recording { get; internal set; }
    public bool RecordPaused { get; internal set; }
    public bool ReplayBufferActive { get; internal set; }
    public bool VirtualCamActive { get; internal set; }
    public bool StudioMode { get; internal set; }
    public ConcurrentDictionary<string, bool> InputMuted { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Minimal obs-websocket v5 client: handshake with SHA-256 auth, request/response
/// correlation and the event subset needed to keep key faces in sync.
/// </summary>
public sealed class ObsWebSocketClient : IAsyncDisposable
{
    private const int RpcVersion = 1;

    // General | Config | Scenes | Inputs | Transitions | Filters | Outputs | SceneItems | MediaInputs | Ui
    private const int EventSubscriptions = 0x3FF;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject?>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _lifecycleLock = new();

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private TaskCompletionSource<bool>? _identified;
    private ObsSettings _settings = new();
    private int _requestId;

    public ObsState State { get; } = new();

    public ObsConnectionState ConnectionState { get; private set; } = ObsConnectionState.Disconnected;

    public string? LastError { get; private set; }

    public event Action? StateChanged;

    public event Action<ObsConnectionState>? ConnectionChanged;

    public bool IsConnected => ConnectionState == ObsConnectionState.Connected;

    public async Task ApplySettingsAsync(ObsSettings settings)
    {
        _settings = settings;
        await DisconnectAsync().ConfigureAwait(false);
        if (settings.Enabled)
        {
            _ = ConnectAsync();
        }
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource cts;
        ClientWebSocket socket;

        lock (_lifecycleLock)
        {
            if (ConnectionState is ObsConnectionState.Connecting or ObsConnectionState.Connected)
            {
                return ConnectionState == ObsConnectionState.Connected;
            }

            _cts?.Dispose();
            cts = _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            socket = _socket = new ClientWebSocket();
            _identified = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetState(ObsConnectionState.Connecting);
        }

        try
        {
            var uri = new Uri($"ws://{_settings.Host}:{_settings.Port}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            await socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);

            _receiveLoop = Task.Run(() => ReceiveLoopAsync(socket, cts.Token), CancellationToken.None);

            var identified = _identified!;
            var completed = await Task.WhenAny(identified.Task, Task.Delay(TimeSpan.FromSeconds(8), cts.Token)).ConfigureAwait(false);
            if (completed != identified.Task || !identified.Task.Result)
            {
                LastError = LastError ?? "OBS did not accept the connection (check the password).";
                await DisconnectAsync().ConfigureAwait(false);
                SetState(ObsConnectionState.Failed);
                return false;
            }

            SetState(ObsConnectionState.Connected);
            LastError = null;
            await RefreshStateAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            SetState(ObsConnectionState.Failed);
            await DisconnectAsync().ConfigureAwait(false);
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        ClientWebSocket? socket;
        CancellationTokenSource? cts;

        lock (_lifecycleLock)
        {
            socket = _socket;
            cts = _cts;
            _socket = null;
            _cts = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (socket is not null)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeTimeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Closing a socket that OBS already dropped is not interesting.
            }
            finally
            {
                socket.Dispose();
            }
        }

        foreach (var pending in _pending.Values)
        {
            pending.TrySetResult(null);
        }

        _pending.Clear();

        if (ConnectionState != ObsConnectionState.Failed)
        {
            SetState(ObsConnectionState.Disconnected);
        }
    }

    /// <summary>Sends a request and waits for its response. Returns null when OBS refuses or is offline.</summary>
    public async Task<JsonObject?> RequestAsync(string requestType, JsonObject? requestData = null, CancellationToken cancellationToken = default)
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            return null;
        }

        var id = Interlocked.Increment(ref _requestId).ToString();
        var tcs = new TaskCompletionSource<JsonObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var payload = new JsonObject
        {
            ["op"] = 6,
            ["d"] = new JsonObject
            {
                ["requestType"] = requestType,
                ["requestId"] = id,
                ["requestData"] = requestData ?? new JsonObject(),
            },
        };

        try
        {
            await SendAsync(socket, payload, cancellationToken).ConfigureAwait(false);
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false);
            return completed == tcs.Task ? tcs.Task.Result : null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async Task<List<string>> GetSceneNamesAsync()
    {
        var response = await RequestAsync("GetSceneList").ConfigureAwait(false);
        return ReadNameArray(response, "scenes", "sceneName");
    }

    public async Task<List<string>> GetInputNamesAsync()
    {
        var response = await RequestAsync("GetInputList").ConfigureAwait(false);
        return ReadNameArray(response, "inputs", "inputName");
    }

    public async Task<List<string>> GetTransitionNamesAsync()
    {
        var response = await RequestAsync("GetSceneTransitionList").ConfigureAwait(false);
        return ReadNameArray(response, "transitions", "transitionName");
    }

    public async Task<List<string>> GetSceneCollectionsAsync()
    {
        var response = await RequestAsync("GetSceneCollectionList").ConfigureAwait(false);
        if (response?["sceneCollections"] is not JsonArray array)
        {
            return new List<string>();
        }

        return array.Select(n => n?.GetValue<string>()).Where(n => n is not null).Select(n => n!).ToList();
    }

    public async Task<List<string>> GetSourcesInSceneAsync(string sceneName)
    {
        var response = await RequestAsync("GetSceneItemList", new JsonObject { ["sceneName"] = sceneName }).ConfigureAwait(false);
        return ReadNameArray(response, "sceneItems", "sourceName");
    }

    public async Task<List<string>> GetFiltersAsync(string sourceName)
    {
        var response = await RequestAsync("GetSourceFilterList", new JsonObject { ["sourceName"] = sourceName }).ConfigureAwait(false);
        return ReadNameArray(response, "filters", "filterName");
    }

    public async Task<int?> GetSceneItemIdAsync(string sceneName, string sourceName)
    {
        var response = await RequestAsync("GetSceneItemId", new JsonObject
        {
            ["sceneName"] = sceneName,
            ["sourceName"] = sourceName,
        }).ConfigureAwait(false);

        return TryGetInt(response, "sceneItemId");
    }

    public async Task RefreshStateAsync()
    {
        var scene = await RequestAsync("GetCurrentProgramScene").ConfigureAwait(false);
        State.CurrentScene = scene?["currentProgramSceneName"]?.GetValue<string>() ?? State.CurrentScene;

        var stream = await RequestAsync("GetStreamStatus").ConfigureAwait(false);
        State.Streaming = stream?["outputActive"]?.GetValue<bool>() ?? State.Streaming;

        var record = await RequestAsync("GetRecordStatus").ConfigureAwait(false);
        State.Recording = record?["outputActive"]?.GetValue<bool>() ?? State.Recording;
        State.RecordPaused = record?["outputPaused"]?.GetValue<bool>() ?? State.RecordPaused;

        var replay = await RequestAsync("GetReplayBufferStatus").ConfigureAwait(false);
        State.ReplayBufferActive = replay?["outputActive"]?.GetValue<bool>() ?? State.ReplayBufferActive;

        var virtualCam = await RequestAsync("GetVirtualCamStatus").ConfigureAwait(false);
        State.VirtualCamActive = virtualCam?["outputActive"]?.GetValue<bool>() ?? State.VirtualCamActive;

        var studio = await RequestAsync("GetStudioModeEnabled").ConfigureAwait(false);
        State.StudioMode = studio?["studioModeEnabled"]?.GetValue<bool>() ?? State.StudioMode;

        StateChanged?.Invoke();
    }

    public async Task<bool?> GetInputMutedAsync(string inputName)
    {
        var response = await RequestAsync("GetInputMute", new JsonObject { ["inputName"] = inputName }).ConfigureAwait(false);
        var muted = response?["inputMuted"]?.GetValue<bool>();
        if (muted is { } value)
        {
            State.InputMuted[inputName] = value;
        }

        return muted;
    }

    private async Task SendAsync(ClientWebSocket socket, JsonNode payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString(Json));
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var message = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var text = Encoding.UTF8.GetString(message.ToArray());
                message.SetLength(0);

                try
                {
                    if (JsonNode.Parse(text) is JsonObject frame)
                    {
                        await HandleFrameAsync(socket, frame, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (JsonException)
                {
                    // Ignore anything that is not valid JSON.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            _identified?.TrySetResult(false);
            if (!cancellationToken.IsCancellationRequested)
            {
                SetState(ObsConnectionState.Disconnected);
                if (_settings.Enabled && _settings.AutoReconnect)
                {
                    _ = ReconnectLaterAsync();
                }
            }
        }
    }

    private async Task ReconnectLaterAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        if (_settings.Enabled && ConnectionState != ObsConnectionState.Connected)
        {
            await ConnectAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleFrameAsync(ClientWebSocket socket, JsonObject frame, CancellationToken cancellationToken)
    {
        var op = frame["op"]?.GetValue<int>();
        var data = frame["d"] as JsonObject;

        switch (op)
        {
            case 0: // Hello
            {
                var identify = new JsonObject
                {
                    ["rpcVersion"] = RpcVersion,
                    ["eventSubscriptions"] = EventSubscriptions,
                };

                if (data?["authentication"] is JsonObject auth)
                {
                    var challenge = auth["challenge"]?.GetValue<string>() ?? string.Empty;
                    var salt = auth["salt"]?.GetValue<string>() ?? string.Empty;
                    identify["authentication"] = BuildAuthentication(_settings.Password, salt, challenge);
                }

                await SendAsync(socket, new JsonObject { ["op"] = 1, ["d"] = identify }, cancellationToken).ConfigureAwait(false);
                break;
            }

            case 2: // Identified
                _identified?.TrySetResult(true);
                break;

            case 5: // Event
                HandleEvent(data);
                break;

            case 7: // RequestResponse
            {
                var id = data?["requestId"]?.GetValue<string>();
                if (id is not null && _pending.TryRemove(id, out var pending))
                {
                    var ok = data?["requestStatus"]?["result"]?.GetValue<bool>() ?? false;
                    if (!ok)
                    {
                        LastError = data?["requestStatus"]?["comment"]?.GetValue<string>() ?? "OBS rejected the request.";
                    }

                    pending.TrySetResult(ok ? data?["responseData"] as JsonObject ?? new JsonObject() : null);
                }

                break;
            }
        }
    }

    private void HandleEvent(JsonObject? data)
    {
        var type = data?["eventType"]?.GetValue<string>();
        var payload = data?["eventData"] as JsonObject;
        if (type is null)
        {
            return;
        }

        switch (type)
        {
            case "CurrentProgramSceneChanged":
                State.CurrentScene = payload?["sceneName"]?.GetValue<string>();
                break;
            case "StreamStateChanged":
                State.Streaming = payload?["outputActive"]?.GetValue<bool>() ?? State.Streaming;
                break;
            case "RecordStateChanged":
                State.Recording = payload?["outputActive"]?.GetValue<bool>() ?? State.Recording;
                break;
            case "RecordStateChangedPaused":
                State.RecordPaused = payload?["outputPaused"]?.GetValue<bool>() ?? State.RecordPaused;
                break;
            case "ReplayBufferStateChanged":
                State.ReplayBufferActive = payload?["outputActive"]?.GetValue<bool>() ?? State.ReplayBufferActive;
                break;
            case "VirtualcamStateChanged":
                State.VirtualCamActive = payload?["outputActive"]?.GetValue<bool>() ?? State.VirtualCamActive;
                break;
            case "StudioModeStateChanged":
                State.StudioMode = payload?["studioModeEnabled"]?.GetValue<bool>() ?? State.StudioMode;
                break;
            case "InputMuteStateChanged":
            {
                var name = payload?["inputName"]?.GetValue<string>();
                var muted = payload?["inputMuted"]?.GetValue<bool>();
                if (name is not null && muted is { } value)
                {
                    State.InputMuted[name] = value;
                }

                break;
            }

            default:
                return;
        }

        StateChanged?.Invoke();
    }

    /// <summary>base64(sha256(base64(sha256(password + salt)) + challenge))</summary>
    internal static string BuildAuthentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private static List<string> ReadNameArray(JsonObject? response, string arrayName, string property)
    {
        if (response?[arrayName] is not JsonArray array)
        {
            return new List<string>();
        }

        return array
            .OfType<JsonObject>()
            .Select(item => item[property]?.GetValue<string>())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();
    }

    private static int? TryGetInt(JsonObject? response, string property)
    {
        var node = response?[property];
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void SetState(ObsConnectionState state)
    {
        if (ConnectionState == state)
        {
            return;
        }

        ConnectionState = state;
        ConnectionChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }
}
