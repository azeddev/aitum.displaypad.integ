using System.Text.Json.Nodes;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Integrations.Obs;

/// <summary>Turns an <see cref="ObsCommand"/> plus its settings into obs-websocket requests.</summary>
public static class ObsCommandRunner
{
    public static async Task<bool> ExecuteAsync(ObsWebSocketClient client, ActionSettings settings, CancellationToken cancellationToken = default)
    {
        if (!client.IsConnected)
        {
            return false;
        }

        switch (settings.ObsCommand)
        {
            case ObsCommand.SetScene:
                if (string.IsNullOrWhiteSpace(settings.ObsScene)) return false;
                return await Send(client, "SetCurrentProgramScene", new JsonObject { ["sceneName"] = settings.ObsScene }, cancellationToken);

            case ObsCommand.ToggleStream:
                return await Send(client, "ToggleStream", null, cancellationToken);
            case ObsCommand.StartStream:
                return await Send(client, "StartStream", null, cancellationToken);
            case ObsCommand.StopStream:
                return await Send(client, "StopStream", null, cancellationToken);

            case ObsCommand.ToggleRecord:
                return await Send(client, "ToggleRecord", null, cancellationToken);
            case ObsCommand.StartRecord:
                return await Send(client, "StartRecord", null, cancellationToken);
            case ObsCommand.StopRecord:
                return await Send(client, "StopRecord", null, cancellationToken);
            case ObsCommand.PauseResumeRecord:
                return await Send(client, "ToggleRecordPause", null, cancellationToken);

            case ObsCommand.ToggleReplayBuffer:
                return await Send(client, "ToggleReplayBuffer", null, cancellationToken);
            case ObsCommand.SaveReplayBuffer:
                return await Send(client, "SaveReplayBuffer", null, cancellationToken);

            case ObsCommand.ToggleVirtualCam:
                return await Send(client, "ToggleVirtualCam", null, cancellationToken);

            case ObsCommand.ToggleStudioMode:
                return await Send(client, "SetStudioModeEnabled",
                    new JsonObject { ["studioModeEnabled"] = !client.State.StudioMode }, cancellationToken);

            case ObsCommand.TriggerStudioTransition:
                return await Send(client, "TriggerStudioModeTransition", null, cancellationToken);

            case ObsCommand.SetTransition:
                if (string.IsNullOrWhiteSpace(settings.ObsTransition)) return false;
                return await Send(client, "SetCurrentSceneTransition",
                    new JsonObject { ["transitionName"] = settings.ObsTransition }, cancellationToken);

            case ObsCommand.SetSceneCollection:
                if (string.IsNullOrWhiteSpace(settings.ObsScene)) return false;
                return await Send(client, "SetCurrentSceneCollection",
                    new JsonObject { ["sceneCollectionName"] = settings.ObsScene }, cancellationToken);

            case ObsCommand.ToggleInputMute:
                if (string.IsNullOrWhiteSpace(settings.ObsSource)) return false;
                return await Send(client, "ToggleInputMute",
                    new JsonObject { ["inputName"] = settings.ObsSource }, cancellationToken);

            case ObsCommand.MuteInput:
            case ObsCommand.UnmuteInput:
                if (string.IsNullOrWhiteSpace(settings.ObsSource)) return false;
                return await Send(client, "SetInputMute", new JsonObject
                {
                    ["inputName"] = settings.ObsSource,
                    ["inputMuted"] = settings.ObsCommand == ObsCommand.MuteInput,
                }, cancellationToken);

            case ObsCommand.RefreshBrowserSource:
                if (string.IsNullOrWhiteSpace(settings.ObsSource)) return false;
                return await Send(client, "PressInputPropertiesButton", new JsonObject
                {
                    ["inputName"] = settings.ObsSource,
                    ["propertyName"] = "refreshnocache",
                }, cancellationToken);

            case ObsCommand.ToggleFilter:
                return await ToggleFilterAsync(client, settings, cancellationToken);

            case ObsCommand.ToggleSourceVisibility:
                return await ToggleSourceVisibilityAsync(client, settings, cancellationToken);

            default:
                return false;
        }
    }

    private static async Task<bool> ToggleFilterAsync(ObsWebSocketClient client, ActionSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ObsSource) || string.IsNullOrWhiteSpace(settings.ObsFilter))
        {
            return false;
        }

        var current = await client.RequestAsync("GetSourceFilter", new JsonObject
        {
            ["sourceName"] = settings.ObsSource,
            ["filterName"] = settings.ObsFilter,
        }, cancellationToken).ConfigureAwait(false);

        if (current is null)
        {
            return false;
        }

        var enabled = current["filterEnabled"]?.GetValue<bool>() ?? false;
        return await Send(client, "SetSourceFilterEnabled", new JsonObject
        {
            ["sourceName"] = settings.ObsSource,
            ["filterName"] = settings.ObsFilter,
            ["filterEnabled"] = !enabled,
        }, cancellationToken);
    }

    private static async Task<bool> ToggleSourceVisibilityAsync(ObsWebSocketClient client, ActionSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ObsSource))
        {
            return false;
        }

        // An empty scene means "whatever is on program right now".
        var scene = string.IsNullOrWhiteSpace(settings.ObsScene) ? client.State.CurrentScene : settings.ObsScene;
        if (string.IsNullOrWhiteSpace(scene))
        {
            return false;
        }

        var itemId = await client.GetSceneItemIdAsync(scene, settings.ObsSource!).ConfigureAwait(false);
        if (itemId is null)
        {
            return false;
        }

        var current = await client.RequestAsync("GetSceneItemEnabled", new JsonObject
        {
            ["sceneName"] = scene,
            ["sceneItemId"] = itemId.Value,
        }, cancellationToken).ConfigureAwait(false);

        if (current is null)
        {
            return false;
        }

        var enabled = current["sceneItemEnabled"]?.GetValue<bool>() ?? false;
        return await Send(client, "SetSceneItemEnabled", new JsonObject
        {
            ["sceneName"] = scene,
            ["sceneItemId"] = itemId.Value,
            ["sceneItemEnabled"] = !enabled,
        }, cancellationToken);
    }

    private static async Task<bool> Send(ObsWebSocketClient client, string request, JsonObject? data, CancellationToken cancellationToken) =>
        await client.RequestAsync(request, data, cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>
    /// Whether the key bound to this command should be drawn in its "on" state
    /// (live scene, streaming, recording, muted, ...).
    /// </summary>
    public static bool IsActive(ObsWebSocketClient client, ActionSettings settings)
    {
        if (!client.IsConnected)
        {
            return false;
        }

        var state = client.State;
        return settings.ObsCommand switch
        {
            ObsCommand.SetScene => string.Equals(state.CurrentScene, settings.ObsScene, StringComparison.Ordinal),
            ObsCommand.ToggleStream or ObsCommand.StartStream => state.Streaming,
            ObsCommand.StopStream => !state.Streaming,
            ObsCommand.ToggleRecord or ObsCommand.StartRecord => state.Recording,
            ObsCommand.StopRecord => !state.Recording,
            ObsCommand.PauseResumeRecord => state.RecordPaused,
            ObsCommand.ToggleReplayBuffer => state.ReplayBufferActive,
            ObsCommand.ToggleVirtualCam => state.VirtualCamActive,
            ObsCommand.ToggleStudioMode => state.StudioMode,
            ObsCommand.ToggleInputMute or ObsCommand.MuteInput =>
                settings.ObsSource is { } input && state.InputMuted.TryGetValue(input, out var muted) && muted,
            _ => false,
        };
    }
}
