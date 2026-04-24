// ✅ REQUIRES: Newtonsoft.Json (install via Package Manager → "com.unity.nuget.newtonsoft-json")
// JsonUtility CANNOT deserialize nested List<> types — this is why boardState.pieces was always null.

using SocketIOClient;
using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

public class CarromNetworkManager : MonoBehaviour
{
    public static CarromNetworkManager Instance { get; private set; }

    [Header("Server Config")]
    [SerializeField] private string serverUrl = "http://localhost:3000";

    private SocketIOUnity _socket;

    public event Action<RoomCreatedData> OnRoomCreated;
    public event Action<RoomJoinedData> OnRoomJoined;
    public event Action<PlayersData> OnPlayerJoined;
    public event Action<GameStartData> OnGameStart;
    public event Action<ShotFiredData> OnShotFired;
    public event Action<TurnUpdateData> OnTurnUpdate;
    public event Action<GameOverData> OnGameOver;
    public event Action<string> OnError;

    public string MySocketId { get; private set; }
    public string CurrentRoomId { get; private set; }
    public bool IsMyTurn { get; private set; }

    private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();

    private void Awake()
    {
        Debug.Log("[NetworkManager:Awake] Initializing singleton");
        if (Instance != null)
        {
            Debug.LogWarning("[NetworkManager:Awake] Duplicate — destroying this one");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[NetworkManager:Awake] ✅ Singleton ready");
    }

    private void Start()
    {
        Debug.Log("[NetworkManager:Start] Calling Connect()");
        Connect();
    }

    private void Update()
    {
        lock (_mainThreadQueue)
        {
            while (_mainThreadQueue.Count > 0)
                _mainThreadQueue.Dequeue()?.Invoke();
        }
    }

    private void RunOnMainThread(Action action)
    {
        lock (_mainThreadQueue)
            _mainThreadQueue.Enqueue(action);
    }

    // ── CONNECTION ──

    public void Connect()
    {
        Debug.Log($"[NetworkManager:Connect] Connecting to: {serverUrl}");
        var uri = new Uri(serverUrl);
        _socket = new SocketIOUnity(uri, new SocketIOOptions
        {
            Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
            EIO = EngineIO.V4
        });

        _socket.OnConnected += (sender, e) =>
        {
            MySocketId = _socket.Id;
            Debug.Log($"[NetworkManager:OnConnected] ✅ Connected! SocketId={MySocketId}");
        };

        _socket.OnDisconnected += (sender, e) =>
        {
            Debug.LogWarning($"[NetworkManager:OnDisconnected] ❌ Disconnected: {e}");
        };

        _socket.OnError += (sender, e) =>
        {
            Debug.LogError($"[NetworkManager:OnSocketError] {e}");
        };

        _socket.OnReconnected += (sender, e) =>
        {
            MySocketId = _socket.Id;
            Debug.Log($"[NetworkManager:OnReconnected] SocketId={MySocketId}");
        };

        RegisterEvents();
        Debug.Log("[NetworkManager:Connect] Calling _socket.Connect()");
        _socket.Connect();
    }

    // ── HELPER: safe JSON parse with Newtonsoft ──
    private T ParseJson<T>(SocketIOResponse response, string eventName)
    {
        // ✅ FIX: Always re-confirm MySocketId before parsing any event
        if (string.IsNullOrEmpty(MySocketId))
        {
            MySocketId = _socket.Id;
            Debug.LogWarning($"[NetworkManager:{eventName}] MySocketId was empty — force-set: {MySocketId}");
        }

        // SocketIOUnity wraps payload in an array: [{{...}}]
        // We need to strip the outer array brackets
        string raw = response.ToString();
        Debug.Log($"[NetworkManager:{eventName}] Raw JSON: {raw}");

        // Strip surrounding array if present
        string json = raw.Trim();
        if (json.StartsWith("[") && json.EndsWith("]"))
            json = json.Substring(1, json.Length - 2).Trim();

        Debug.Log($"[NetworkManager:{eventName}] Cleaned JSON: {json}");
        return JsonConvert.DeserializeObject<T>(json);
    }

    // ── REGISTER EVENTS ──

    private void RegisterEvents()
    {
        Debug.Log("[NetworkManager:RegisterEvents] Registering listeners...");

        _socket.On("room_created", response =>
        {
            try
            {
                var data = ParseJson<RoomCreatedData>(response, "room_created");
                CurrentRoomId = data.roomId;
                Debug.Log($"[NetworkManager:room_created] ✅ roomId={data.roomId} bet={data.betAmount} myId={MySocketId}");
                RunOnMainThread(() => OnRoomCreated?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:room_created] ❌ {ex.Message}\n{ex.StackTrace}"); }
        });

        _socket.On("room_joined", response =>
        {
            try
            {
                var data = ParseJson<RoomJoinedData>(response, "room_joined");
                CurrentRoomId = data.roomId;
                Debug.Log($"[NetworkManager:room_joined] ✅ roomId={data.roomId} myId={MySocketId}");
                RunOnMainThread(() => OnRoomJoined?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:room_joined] ❌ {ex.Message}"); }
        });

        _socket.On("player_joined", response =>
        {
            try
            {
                var data = ParseJson<PlayersData>(response, "player_joined");
                Debug.Log($"[NetworkManager:player_joined] Players: {data.players?.Count}");
                RunOnMainThread(() => OnPlayerJoined?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:player_joined] ❌ {ex.Message}"); }
        });

        _socket.On("game_start", response =>
        {
            try
            {
                var data = ParseJson<GameStartData>(response, "game_start");

                // ✅ CRITICAL: IsMyTurn must be set AFTER MySocketId is confirmed
                IsMyTurn = data.currentTurn == MySocketId;

                Debug.Log($"[NetworkManager:game_start] currentTurn={data.currentTurn}");
                Debug.Log($"[NetworkManager:game_start] MySocketId={MySocketId}");
                Debug.Log($"[NetworkManager:game_start] IsMyTurn={IsMyTurn}  ← MUST be correct for both players");
                Debug.Log($"[NetworkManager:game_start] Players count={data.players?.Count}");
                Debug.Log($"[NetworkManager:game_start] Board pieces={data.boardState?.pieces?.Count}");

                if (data.players != null)
                    foreach (var p in data.players)
                        Debug.Log($"[NetworkManager:game_start]   Player: id={p.id} name={p.name} isHost={p.isHost}");

                if (data.boardState?.pieces == null || data.boardState.pieces.Count == 0)
                    Debug.LogError("[NetworkManager:game_start] ❌ boardState.pieces is NULL or EMPTY — JSON parse failed!");

                RunOnMainThread(() => OnGameStart?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:game_start] ❌ {ex.Message}\n{ex.StackTrace}"); }
        });

        _socket.On("shot_fired", response =>
        {
            try
            {
                var data = ParseJson<ShotFiredData>(response, "shot_fired");
                bool isOpponent = data.playerId != MySocketId;
                Debug.Log($"[NetworkManager:shot_fired] playerId={data.playerId} isOpponent={isOpponent} angle={data.angle} power={data.power}");

                if (isOpponent)
                {
                    Debug.Log("[NetworkManager:shot_fired] Dispatching opponent shot");
                    RunOnMainThread(() => OnShotFired?.Invoke(data));
                }
                else
                {
                    Debug.Log("[NetworkManager:shot_fired] My own shot echoed back — ignoring");
                }
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:shot_fired] ❌ {ex.Message}"); }
        });

        _socket.On("turn_update", response =>
        {
            try
            {
                var data = ParseJson<TurnUpdateData>(response, "turn_update");
                IsMyTurn = data.currentTurn == MySocketId;
                Debug.Log($"[NetworkManager:turn_update] currentTurn={data.currentTurn} MySocketId={MySocketId} IsMyTurn={IsMyTurn}");

                if (data.scores != null)
                    foreach (var s in data.scores)
                        Debug.Log($"[NetworkManager:turn_update]   Score: {s.name}={s.score}");

                if (data.boardState?.pieces == null)
                    Debug.LogWarning("[NetworkManager:turn_update] boardState.pieces is null");

                RunOnMainThread(() => OnTurnUpdate?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:turn_update] ❌ {ex.Message}"); }
        });

        _socket.On("game_over", response =>
        {
            try
            {
                var data = ParseJson<GameOverData>(response, "game_over");
                Debug.Log($"[NetworkManager:game_over] winner={data.winnerName} pot={data.totalPot}");
                RunOnMainThread(() => OnGameOver?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:game_over] ❌ {ex.Message}"); }
        });

        _socket.On("board_sync", response =>
        {
            Debug.Log("[NetworkManager:board_sync] Received (not yet handled)");
        });

        _socket.On("error", response =>
        {
            try
            {
                var data = ParseJson<ErrorData>(response, "error");
                Debug.LogError($"[NetworkManager:error] Server: {data.message}");
                RunOnMainThread(() => OnError?.Invoke(data.message));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:error] ❌ {ex.Message}"); }
        });

        Debug.Log("[NetworkManager:RegisterEvents] ✅ All events registered");
    }

    // ── EMIT METHODS ──

    public void CreateRoom(string playerName, int betAmount)
    {
        Debug.Log($"[NetworkManager:CreateRoom] name={playerName} bet={betAmount} myId={MySocketId}");
        _socket.Emit("create_room", new { playerName, betAmount });
    }

    public void JoinRoom(string roomId, string playerName, int betAmount)
    {
        Debug.Log($"[NetworkManager:JoinRoom] roomId={roomId} name={playerName} bet={betAmount} myId={MySocketId}");
        _socket.Emit("join_room", new { roomId, playerName, betAmount });
    }

    public void SendShot(float angle, float power)
    {
        Debug.Log($"[NetworkManager:SendShot] IsMyTurn={IsMyTurn} angle={angle:F2} power={power:F3}");
        if (!IsMyTurn)
        {
            Debug.LogWarning("[NetworkManager:SendShot] ❌ BLOCKED — not my turn");
            return;
        }
        _socket.Emit("striker_shot", new { angle, power });
        Debug.Log("[NetworkManager:SendShot] ✅ striker_shot emitted");
    }

    public void SendShotResult(List<string> pocketedPieceIds, bool strikerPocketed)
    {
        Debug.Log($"[NetworkManager:SendShotResult] pocketed=[{string.Join(",", pocketedPieceIds)}] strikerPocketed={strikerPocketed}");
        _socket.Emit("shot_result", new { pocketedPieces = pocketedPieceIds, strikerPocketed });
        Debug.Log("[NetworkManager:SendShotResult] ✅ shot_result emitted");
    }

    public void RequestSync()
    {
        Debug.Log("[NetworkManager:RequestSync] Emitting request_sync");
        _socket.Emit("request_sync");
    }

    private void OnDestroy()
    {
        Debug.Log("[NetworkManager:OnDestroy] Disconnecting");
        _socket?.Disconnect();
    }
}

// ── DATA MODELS — use [JsonProperty] to match server camelCase keys ──

[Serializable]
public class RoomCreatedData
{
    [JsonProperty("roomId")] public string roomId;
    [JsonProperty("player")] public PlayerData player;
    [JsonProperty("betAmount")] public int betAmount;
}
[Serializable]
public class RoomJoinedData
{
    [JsonProperty("roomId")] public string roomId;
    [JsonProperty("player")] public PlayerData player;
    [JsonProperty("betAmount")] public int betAmount;
}
[Serializable]
public class PlayersData
{
    [JsonProperty("players")] public List<PlayerData> players;
}
[Serializable]
public class PlayerData
{
    [JsonProperty("id")] public string id;
    [JsonProperty("name")] public string name;
    [JsonProperty("coins")] public int coins;
    [JsonProperty("score")] public int score;
    [JsonProperty("isHost")] public bool isHost;
}
[Serializable]
public class GameStartData
{
    [JsonProperty("currentTurn")] public string currentTurn;
    [JsonProperty("players")] public List<PlayerData> players;
    [JsonProperty("boardState")] public BoardStateData boardState;
    [JsonProperty("betAmount")] public int betAmount;
    [JsonProperty("winScore")] public int winScore;
}
[Serializable]
public class ShotFiredData
{
    [JsonProperty("playerId")] public string playerId;
    [JsonProperty("angle")] public float angle;
    [JsonProperty("power")] public float power;
}
[Serializable]
public class TurnUpdateData
{
    [JsonProperty("currentTurn")] public string currentTurn;
    [JsonProperty("scores")] public List<PlayerData> scores;
    [JsonProperty("boardState")] public BoardStateData boardState;
}
[Serializable]
public class GameOverData
{
    [JsonProperty("winnerId")] public string winnerId;
    [JsonProperty("winnerName")] public string winnerName;
    [JsonProperty("loserName")] public string loserName;
    [JsonProperty("totalPot")] public int totalPot;
    [JsonProperty("message")] public string message;
}
[Serializable]
public class ErrorData
{
    [JsonProperty("message")] public string message;
}
[Serializable]
public class BoardStateData
{
    [JsonProperty("pieces")] public List<PieceData> pieces;
}
[Serializable]
public class PieceData
{
    [JsonProperty("id")] public string id;
    [JsonProperty("type")] public string type;
    [JsonProperty("x")] public float x;
    [JsonProperty("y")] public float y;
    [JsonProperty("active")] public bool active;
}