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

        void Update()
        {
            MultiplayerSession.Pump();
        }

        void OnGUI()
        {
            if (_isCollapsed)
            {
                var label = MultiplayerSession.IsConnected ? "Multiplayer (Connected)" : "Multiplayer";
                if (GUI.Button(new Rect(20, 20, 160, 30), label))
                {
                    _isCollapsed = false;
                }
                return;
            }

            var panel = new Rect(20, 20, 330, MultiplayerSession.IsConnected ? 150 : 250);
            GUI.Box(panel, "Multiplayer");

            if (GUI.Button(new Rect(panel.xMax - 75, panel.y + 4, 70, 20), "Minimize"))
            {
                _isCollapsed = true;
                return;
            }

            GUILayout.BeginArea(new Rect(35, 50, 300, panel.height - 60));
            GUILayout.Label(_status);
            if (MultiplayerSession.IsConnected)
            {
                GUILayout.Label("Connected - song selection is shared.");
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