using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using WebSocketSharp;

#nullable enable
namespace MajdataPlay.Net
{
    internal static class MultiplayerSession
    {
        public static bool IsConnected => _socket?.IsAlive == true;
        public static bool IsApplyingRemoteSelection { get; private set; }
        public static string? SelectedSongHash { get; private set; }
        public static long? ScheduledStartAtServerMs { get; private set; }
        public static event Action<string>? SongSelected;
        public static event Action? StartScheduled;

        static readonly ConcurrentQueue<Action> _mainThreadActions = new();
        static readonly Stopwatch _clock = Stopwatch.StartNew();
        static WebSocket? _socket;
        static double _serverOffsetMs;
        static double _lowestRoundTripMs = double.MaxValue;

        public static async UniTask ConnectAsync(Uri websocketUri)
        {
            Disconnect();
            var socket = new WebSocket(websocketUri.ToString());
            socket.OnMessage += (_, args) => Receive(args.Data);
            socket.OnClose += (_, _) => _mainThreadActions.Enqueue(() => ScheduledStartAtServerMs = null);
            _socket = socket;
            await UniTask.RunOnThreadPool(socket.Connect);
            SendClockPing();
        }

        public static void Disconnect()
        {
            _socket?.Close();
            _socket = null;
            SelectedSongHash = null;
            ScheduledStartAtServerMs = null;
        }

        public static void Pump()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                action();
            }
        }

        public static void PublishSongSelection(string songHash)
        {
            if (!IsConnected || IsApplyingRemoteSelection || songHash == SelectedSongHash)
            {
                return;
            }
            Send(new JObject { ["type"] = "select_song", ["songHash"] = songHash });
        }

        public static void SetDifficulty(int difficulty) => Send(new JObject { ["type"] = "set_difficulty", ["difficulty"] = difficulty });
        public static void SetReady(bool ready) => Send(new JObject { ["type"] = "set_ready", ["ready"] = ready });
        public static void RequestStart() => Send(new JObject { ["type"] = "request_start" });

        public static float GetSecondsUntilScheduledStart()
        {
            if (ScheduledStartAtServerMs is null)
            {
                return -1f;
            }
            return (float)((ScheduledStartAtServerMs.Value - ServerNowMs) / 1000d);
        }

        static double LocalNowMs => _clock.Elapsed.TotalMilliseconds;
        static double ServerNowMs => LocalNowMs + _serverOffsetMs;

        static void SendClockPing()
        {
            Send(new JObject { ["type"] = "clock_ping", ["clientTimeMs"] = LocalNowMs });
        }

        static void Send(JObject message)
        {
            if (IsConnected)
            {
                _socket!.Send(message.ToString(Newtonsoft.Json.Formatting.None));
            }
        }

        static void Receive(string rawMessage)
        {
            try
            {
                var message = JObject.Parse(rawMessage);
                if (message.Value<string>("type") == "clock_pong")
                {
                    var sentAt = message.Value<double?>("clientTimeMs");
                    var serverAt = message.Value<double?>("serverTimeMs");
                    if (sentAt is not null && serverAt is not null)
                    {
                        var receivedAt = LocalNowMs;
                        var rtt = receivedAt - sentAt.Value;
                        if (rtt < _lowestRoundTripMs)
                        {
                            _lowestRoundTripMs = rtt;
                            _serverOffsetMs = serverAt.Value - (sentAt.Value + rtt / 2d);
                        }
                    }
                    return;
                }
                var room = message["room"] as JObject;
                var songHash = room?.Value<string>("songHash");
                var startAt = room?.Value<long?>("startAtMs");
                _mainThreadActions.Enqueue(() => ApplyRoomState(songHash, startAt));
            }
            catch
            {
                // A malformed multiplayer message must not interrupt gameplay.
            }
        }

        static void ApplyRoomState(string? songHash, long? startAt)
        {
            if (!string.IsNullOrEmpty(songHash) && songHash != SelectedSongHash)
            {
                SelectedSongHash = songHash;
                IsApplyingRemoteSelection = true;
                try
                {
                    SongSelected?.Invoke(songHash);
                }
                finally
                {
                    IsApplyingRemoteSelection = false;
                }
            }
            if (startAt is null)
            {
                ScheduledStartAtServerMs = null;
            }
            else if (startAt != ScheduledStartAtServerMs)
            {
                ScheduledStartAtServerMs = startAt;
                StartScheduled?.Invoke();
            }
        }
    }
}
