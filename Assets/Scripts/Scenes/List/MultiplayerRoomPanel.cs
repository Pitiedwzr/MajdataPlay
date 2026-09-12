using Cysharp.Threading.Tasks;
using MajdataPlay.Net;
using System;
using System.Linq;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Scenes.List
{
    internal sealed class MultiplayerRoomPanel : MonoBehaviour
    {
        string _roomName = "Multiplayer room";
        string _roomCode = string.Empty;
        string _status = "Create a room or enter a six-character room code.";
        bool _isSubmitting;
        bool _isCollapsed;
        Rect _windowRect = new(20, 20, 330, 250);
        const int WINDOW_ID = 0x4D504C;

        void OnGUI()
        {
            if (_isCollapsed)
            {
                var label = MultiplayerSession.IsConnected ? "Multiplayer (Connected)" : "Multiplayer";
                if (GUI.Button(new Rect(_windowRect.x, _windowRect.y, 160, 30), label))
                {
                    _isCollapsed = false;
                }
                return;
            }

            _windowRect.height = MultiplayerSession.IsConnected ? 190 : 250;
            _windowRect = GUI.Window(WINDOW_ID, _windowRect, DrawWindow, "Multiplayer");
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Mathf.Max(0, Screen.width - _windowRect.width));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Mathf.Max(0, Screen.height - _windowRect.height));
        }

        void DrawWindow(int windowId)
        {
            if (GUI.Button(new Rect(_windowRect.width - 75, 2, 70, 20), "Minimize"))
            {
                _isCollapsed = true;
                return;
            }

            GUILayout.BeginArea(new Rect(15, 30, _windowRect.width - 30, _windowRect.height - 40));
            GUILayout.Label(_status);
            if (MultiplayerSession.IsConnected)
            {
                DisplayRoomStatus();
                if (GUILayout.Button("Leave room"))
                {
                    MultiplayerSession.Disconnect();
                    _status = "Left room.";
                }
            }
            else
            {
                GUILayout.Label("Room name");
                _roomName = GUILayout.TextField(_roomName, 48);
                GUI.enabled = !_isSubmitting;
                if (GUILayout.Button("Create room"))
                {
                    CreateRoomAsync().Forget();
                }
                GUILayout.Label("Room code");
                _roomCode = GUILayout.TextField(_roomCode, 6).ToUpperInvariant();
                if (GUILayout.Button("Join room"))
                {
                    JoinRoomAsync().Forget();
                }
                GUI.enabled = true;
            }
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, _windowRect.width - 80, 25));
        }

        static void DisplayRoomStatus()
        {
            var room = MultiplayerSession.CurrentRoom;
            if (room is null)
            {
                GUILayout.Label("Connected - waiting for room status.");
                return;
            }
            GUILayout.Label($"{room.Value.Name} ({room.Value.Code})");
            GUILayout.Label($"Players: {room.Value.PlayerCount}  Ready: {room.Value.ReadyPlayerCount}/{room.Value.PlayerCount}");
            foreach (var member in room.Value.Members)
            {
                var status = !member.IsConnected ? "Disconnected" : member.IsRoundComplete ? "Finished" : member.IsReady ? "Ready" : "Selecting";
                GUILayout.Label($"{member.Username} - {status}");
            }
        }

        async UniTaskVoid CreateRoomAsync()
        {
            await SubmitAsync(() => MultiplayerSession.CreateRoomAsync(GetEndpoint(), _roomName));
        }

        async UniTaskVoid JoinRoomAsync()
        {
            if (_roomCode.Length != 6)
            {
                _status = "Room codes contain six characters.";
                return;
            }
            await SubmitAsync(() => MultiplayerSession.JoinRoomAsync(GetEndpoint(), _roomCode));
        }

        async UniTask SubmitAsync(Func<UniTask<MultiplayerSession.RoomInfo>> action)
        {
            try
            {
                _isSubmitting = true;
                var room = await action();
                _status = $"Connected to {room.Name} ({room.Code}).";
                _isCollapsed = true;
            }
            catch (Exception exception)
            {
                _status = $"Multiplayer failed: {exception.Message}";
            }
            finally
            {
                _isSubmitting = false;
            }
        }

        static ApiEndpoint GetEndpoint()
        {
            return MajEnv.ApiEndpoints.FirstOrDefault(endpoint => endpoint.RuntimeConfig.IsLoggedIn)
                ?? throw new InvalidOperationException("Log in to an online endpoint before joining multiplayer.");
        }
    }
}
