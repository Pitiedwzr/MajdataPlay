using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;

#nullable enable
namespace MajdataPlay.Net
{
    internal static class MultiplayerSession
    {
        public readonly struct RoomMember
        {
            public string UserId { get; init; }
            public string Username { get; init; }
            public int Difficulty { get; init; }
            public bool IsReady { get; init; }
            public bool IsConnected { get; init; }
            public bool IsRoundComplete { get; init; }
        }

        public readonly struct RoomState
        {
            public string Id { get; init; }
            public string Code { get; init; }
            public string Name { get; init; }
            public string? SongHash { get; init; }
            public string Phase { get; init; }
            public RoomMember[] Members { get; init; }
            public int PlayerCount => Members?.Length ?? 0;
            public int ReadyPlayerCount
            {
                get
                {
                    var count = 0;
                    foreach (var member in Members ?? Array.Empty<RoomMember>())
                    {
                        if (member.IsReady)
                        {
                            count++;
                        }
                    }
                    return count;
                }
            }
        }

        public readonly struct RoomInfo
        {
            public string Id { get; init; }
            public string Code { get; init; }
            public string Name { get; init; }
        }
        // WebSocket.IsAlive sends a ping and synchronously waits for a pong. This
        // property is used by UI and input code, so it must only inspect local state.
        public static bool IsConnected => _socket?.ReadyState == WebSocketState.Open;
        public static bool IsApplyingRemoteSelection { get; private set; }
        public static string? SelectedSongHash { get; private set; }
        public static long? ScheduledStartAtServerMs { get; private set; }
        public static RoomState? CurrentRoom { get; private set; }
        public static event Action<string>? SongSelected;
        public static event Action? StartScheduled;
        public static event Action<RoomState>? RoomStateChanged;

        static readonly ConcurrentQueue<Action> _mainThreadActions = new();
        const int MAX_MAIN_THREAD_ACTIONS_PER_FRAME = 64;
        const double INITIAL_CLOCK_SYNC_INTERVAL_MS = 1_000d;
        const double CLOCK_SYNC_INTERVAL_MS = 5_000d;
        static readonly Stopwatch _clock = Stopwatch.StartNew();
        static WebSocket? _socket;
        static double _serverOffsetMs;
        static double _lowestRoundTripMs = double.MaxValue;
        static int _clockSyncSampleCount;
        static double _nextClockPingAtMs;

        public static async UniTask ConnectAsync(Uri websocketUri)
        {
            Disconnect();
            var socket = new WebSocket(websocketUri.ToString());
            socket.OnMessage += (_, args) => Receive(args.Data);
            socket.OnClose += (_, _) => _mainThreadActions.Enqueue(() => ScheduledStartAtServerMs = null);
            _socket = socket;
            await UniTask.RunOnThreadPool(socket.Connect);
            _lowestRoundTripMs = double.MaxValue;
            _clockSyncSampleCount = 0;
            SendClockPing();
        }

        public static async UniTask<RoomInfo> CreateRoomAsync(ApiEndpoint endpoint, string roomName)
        {
            var content = new StringContent(new JObject { ["name"] = roomName }.ToString(), Encoding.UTF8, "application/json");
            using var response = await MajEnv.SharedHttpClient.PostAsync(new Uri(endpoint.Url, "multiplayer/rooms"), content);
            response.EnsureSuccessStatusCode();
            return await ConnectToRoomAsync(endpoint, JObject.Parse(await response.Content.ReadAsStringAsync()));
        }

        public static async UniTask<RoomInfo> JoinRoomAsync(ApiEndpoint endpoint, string roomCode)
        {
            var content = new StringContent(new JObject { ["code"] = roomCode.Trim().ToUpperInvariant() }.ToString(), Encoding.UTF8, "application/json");
            using var response = await MajEnv.SharedHttpClient.PostAsync(new Uri(endpoint.Url, "multiplayer/rooms/join"), content);
            response.EnsureSuccessStatusCode();
            return await ConnectToRoomAsync(endpoint, JObject.Parse(await response.Content.ReadAsStringAsync()));
        }

        public static void Disconnect()
        {
            _socket?.Close();
            _socket = null;
            SelectedSongHash = null;
            ScheduledStartAtServerMs = null;
            CurrentRoom = null;
            _clockSyncSampleCount = 0;
            _nextClockPingAtMs = 0;
        }

        public static void Pump()
        {
            for (var i = 0; i < MAX_MAIN_THREAD_ACTIONS_PER_FRAME
                && _mainThreadActions.TryDequeue(out var action); i++)
            {
                action();
            }
            if (IsConnected && LocalNowMs >= _nextClockPingAtMs)
            {
                SendClockPing();
            }
        }

        public static void PublishSongSelection(string songHash)
        {
            if (!IsConnected || IsApplyingRemoteSelection || songHash == SelectedSongHash)
            {
                return;
            }
            SelectedSongHash = songHash;
            Send(new JObject { ["type"] = "select_song", ["songHash"] = songHash });
        }

        public static void SetDifficulty(int difficulty) => Send(new JObject { ["type"] = "set_difficulty", ["difficulty"] = difficulty });
        public static void SetReady(bool ready) => Send(new JObject { ["type"] = "set_ready", ["ready"] = ready });
        public static void RequestStart() => Send(new JObject { ["type"] = "request_start" });
        public static void CompleteRound() => Send(new JObject { ["type"] = "complete_round" });

        public static float GetSecondsUntilScheduledStart()
        {
            if (ScheduledStartAtServerMs is null)
            {
                return -1f;
            }
            return (float)((ScheduledStartAtServerMs.Value - ServerNowMs) / 1000d);
        }

        public static string GetWaitingForPlayersText()
        {
            var room = CurrentRoom;
            if (room is null)
            {
                return "Waiting for multiplayer players...";
            }
            var waitingPlayers = new System.Collections.Generic.List<string>();
            foreach (var member in room.Value.Members ?? Array.Empty<RoomMember>())
            {
                if (!member.IsReady)
                {
                    waitingPlayers.Add(member.Username);
                }
            }
            var status = $"Waiting for players ({room.Value.ReadyPlayerCount}/{room.Value.PlayerCount})";
            return waitingPlayers.Count == 0 ? status : $"{status}: {string.Join(", ", waitingPlayers)}";
        }

        static double LocalNowMs => _clock.Elapsed.TotalMilliseconds;
        static double ServerNowMs => LocalNowMs + _serverOffsetMs;

        static void SendClockPing()
        {
            Send(new JObject { ["type"] = "clock_ping", ["clientTimeMs"] = LocalNowMs });
            _nextClockPingAtMs = LocalNowMs + (_clockSyncSampleCount < 3
                ? INITIAL_CLOCK_SYNC_INTERVAL_MS
                : CLOCK_SYNC_INTERVAL_MS);
        }

        static async UniTask<RoomInfo> ConnectToRoomAsync(ApiEndpoint endpoint, JObject room)
        {
            var roomId = room.Value<string>("roomId") ?? throw new InvalidOperationException("Room response is missing roomId.");
            var content = new StringContent(new JObject { ["room_id"] = roomId }.ToString(), Encoding.UTF8, "application/json");
            using var response = await MajEnv.SharedHttpClient.PostAsync(new Uri(endpoint.Url, "multiplayer/rooms/ticket"), content);
            response.EnsureSuccessStatusCode();
            var ticket = JObject.Parse(await response.Content.ReadAsStringAsync()).Value<string>("ticket")
                ?? throw new InvalidOperationException("Room ticket response is missing ticket.");
            var builder = new UriBuilder(endpoint.Url)
            {
                Scheme = endpoint.Url.Scheme == "https" ? "wss" : "ws",
                Path = endpoint.Url.AbsolutePath.TrimEnd('/') + "/multiplayer/ws",
                Query = $"ticket={Uri.EscapeDataString(ticket)}&room_id={Uri.EscapeDataString(roomId)}",
            };
            await ConnectAsync(builder.Uri);
            return new RoomInfo
            {
                Id = roomId,
                Code = room.Value<string>("code") ?? string.Empty,
                Name = room.Value<string>("name") ?? string.Empty,
            };
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
                        _clockSyncSampleCount++;
                    }
                    return;
                }
                var room = message["room"] as JObject;
                if (room is null)
                {
                    return;
                }
                var roomState = ParseRoomState(room);
                var startAt = room.Value<long?>("startAtMs");
                _mainThreadActions.Enqueue(() => ApplyRoomState(roomState, startAt));
            }
            catch
            {
                // A malformed multiplayer message must not interrupt gameplay.
            }
        }

        static RoomState ParseRoomState(JObject room)
        {
            var members = room["members"] as JArray;
            var parsedMembers = members is null ? Array.Empty<RoomMember>() : new RoomMember[members.Count];
            if (members is not null)
            {
                for (var i = 0; i < members.Count; i++)
                {
                    var member = members[i] as JObject;
                    parsedMembers[i] = new RoomMember
                    {
                        UserId = member?.Value<string>("userId") ?? string.Empty,
                        Username = member?.Value<string>("username") ?? "Unknown player",
                        Difficulty = member?.Value<int?>("difficulty") ?? 0,
                        IsReady = member?.Value<bool?>("ready") ?? false,
                        IsConnected = member?.Value<bool?>("connected") ?? true,
                        IsRoundComplete = member?.Value<bool?>("roundComplete") ?? false,
                    };
                }
            }
            return new RoomState
            {
                Id = room.Value<string>("roomId") ?? string.Empty,
                Code = room.Value<string>("code") ?? string.Empty,
                Name = room.Value<string>("name") ?? "Multiplayer room",
                SongHash = room.Value<string>("songHash"),
                Phase = room.Value<string>("phase") ?? "lobby",
                Members = parsedMembers,
            };
        }

        static void ApplyRoomState(RoomState room, long? startAt)
        {
            CurrentRoom = room;
            RoomStateChanged?.Invoke(room);
            var songHash = room.SongHash;
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
