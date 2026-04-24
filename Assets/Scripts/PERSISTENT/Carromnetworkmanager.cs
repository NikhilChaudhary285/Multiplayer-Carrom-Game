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

    // ✅ NEW: Opponent position sync event
    public event Action<SyncPositionsData> OnSyncPositions;

    public string MySocketId { get; private set; }
    public string CurrentRoomId { get; private set; }
    public bool IsMyTurn { get; private set; }

    private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();

    private void Awake()
    {
        Debug.Log("[NetworkManager:Awake] Initializing singleton");
        if (Instance != null) { Destroy(gameObject); return; }
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
            while (_mainThreadQueue.Count > 0)
                _mainThreadQueue.Dequeue()?.Invoke();
    }

    private void RunOnMainThread(Action action)
    {
        lock (_mainThreadQueue)
            _mainThreadQueue.Enqueue(action);
    }

    public void Connect()
    {
        Debug.Log($"[NetworkManager:Connect] Connecting to: {serverUrl}");
        var uri = new Uri(serverUrl);
        _socket = new SocketIOUnity(uri, new SocketIOOptions
        {
            Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
            EIO = EngineIO.V4
        });

        _socket.OnConnected += (s, e) => { MySocketId = _socket.Id; Debug.Log($"[NetworkManager:OnConnected] ✅ Id={MySocketId}"); };
        _socket.OnDisconnected += (s, e) => Debug.LogWarning($"[NetworkManager:OnDisconnected] ❌ {e}");
        _socket.OnError += (s, e) => Debug.LogError($"[NetworkManager:OnSocketError] {e}");
        _socket.OnReconnected += (s, e) => { MySocketId = _socket.Id; Debug.Log($"[NetworkManager:OnReconnected] Id={MySocketId}"); };

        RegisterEvents();
        _socket.Connect();
        Debug.Log("[NetworkManager:Connect] _socket.Connect() called");
    }

    private T ParseJson<T>(SocketIOResponse response, string eventName)
    {
        if (string.IsNullOrEmpty(MySocketId))
        {
            MySocketId = _socket.Id;
            Debug.LogWarning($"[NetworkManager:{eventName}] MySocketId empty — force-set: {MySocketId}");
        }
        string raw = response.ToString();
        string json = raw.Trim();
        if (json.StartsWith("[") && json.EndsWith("]"))
            json = json.Substring(1, json.Length - 2).Trim();
        Debug.Log($"[NetworkManager:{eventName}] JSON: {json}");
        return JsonConvert.DeserializeObject<T>(json);
    }

    private void RegisterEvents()
    {
        Debug.Log("[NetworkManager:RegisterEvents] Registering...");

        _socket.On("room_created", response =>
        {
            try
            {
                var data = ParseJson<RoomCreatedData>(response, "room_created");
                CurrentRoomId = data.roomId;
                Debug.Log($"[NetworkManager:room_created] roomId={data.roomId}");
                RunOnMainThread(() => OnRoomCreated?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:room_created] ❌ {ex.Message}"); }
        });

        _socket.On("room_joined", response =>
        {
            try
            {
                var data = ParseJson<RoomJoinedData>(response, "room_joined");
                CurrentRoomId = data.roomId;
                Debug.Log($"[NetworkManager:room_joined] roomId={data.roomId}");
                RunOnMainThread(() => OnRoomJoined?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:room_joined] ❌ {ex.Message}"); }
        });

        _socket.On("player_joined", response =>
        {
            try
            {
                var data = ParseJson<PlayersData>(response, "player_joined");
                Debug.Log($"[NetworkManager:player_joined] count={data.players?.Count}");
                RunOnMainThread(() => OnPlayerJoined?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:player_joined] ❌ {ex.Message}"); }
        });

        _socket.On("game_start", response =>
        {
            try
            {
                var data = ParseJson<GameStartData>(response, "game_start");
                if (string.IsNullOrEmpty(MySocketId)) MySocketId = _socket.Id;
                IsMyTurn = data.currentTurn == MySocketId;
                Debug.Log($"[NetworkManager:game_start] currentTurn={data.currentTurn} MyId={MySocketId} IsMyTurn={IsMyTurn} pieces={data.boardState?.pieces?.Count}");
                if (data.players != null)
                    foreach (var p in data.players)
                        Debug.Log($"[NetworkManager:game_start]   Player id={p.id} name={p.name} isHost={p.isHost}");
                if (data.boardState?.pieces == null || data.boardState.pieces.Count == 0)
                    Debug.LogError("[NetworkManager:game_start] ❌ boardState.pieces NULL/EMPTY — Newtonsoft not working!");
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
                Debug.Log($"[NetworkManager:shot_fired] from={data.playerId} isOpponent={isOpponent}");
                if (isOpponent)
                    RunOnMainThread(() => OnShotFired?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:shot_fired] ❌ {ex.Message}"); }
        });

        // ✅ NEW: Receive real-time position sync from opponent
        _socket.On("sync_positions", response =>
        {
            try
            {
                // Parse without logging (fires every frame)
                string raw = response.ToString().Trim();
                if (raw.StartsWith("[") && raw.EndsWith("]"))
                    raw = raw.Substring(1, raw.Length - 2).Trim();
                var data = JsonConvert.DeserializeObject<SyncPositionsData>(raw);
                // Only apply if this came from the opponent (server already filters, but double-check)
                RunOnMainThread(() => OnSyncPositions?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:sync_positions] ❌ {ex.Message}"); }
        });

        _socket.On("turn_update", response =>
        {
            try
            {
                var data = ParseJson<TurnUpdateData>(response, "turn_update");
                if (string.IsNullOrEmpty(MySocketId)) MySocketId = _socket.Id;
                IsMyTurn = data.currentTurn == MySocketId;
                Debug.Log($"[NetworkManager:turn_update] currentTurn={data.currentTurn} IsMyTurn={IsMyTurn}");
                if (data.scores != null)
                    foreach (var s in data.scores)
                        Debug.Log($"[NetworkManager:turn_update]   {s.name}={s.score}");
                RunOnMainThread(() => OnTurnUpdate?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:turn_update] ❌ {ex.Message}"); }
        });

        _socket.On("game_over", response =>
        {
            try
            {
                var data = ParseJson<GameOverData>(response, "game_over");
                Debug.Log($"[NetworkManager:game_over] winner={data.winnerName}");
                RunOnMainThread(() => OnGameOver?.Invoke(data));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:game_over] ❌ {ex.Message}"); }
        });

        _socket.On("board_sync", response =>
        {
            Debug.Log("[NetworkManager:board_sync] Received");
        });

        _socket.On("error", response =>
        {
            try
            {
                var data = ParseJson<ErrorData>(response, "error");
                Debug.LogError($"[NetworkManager:error] {data.message}");
                RunOnMainThread(() => OnError?.Invoke(data.message));
            }
            catch (Exception ex) { Debug.LogError($"[NetworkManager:error] ❌ {ex.Message}"); }
        });

        Debug.Log("[NetworkManager:RegisterEvents] ✅ Done");
    }

    // ── EMIT METHODS ──

    public void CreateRoom(string playerName, int betAmount)
    {
        Debug.Log($"[NetworkManager:CreateRoom] name={playerName} bet={betAmount}");
        _socket.Emit("create_room", new { playerName, betAmount });
    }

    public void JoinRoom(string roomId, string playerName, int betAmount)
    {
        Debug.Log($"[NetworkManager:JoinRoom] room={roomId} name={playerName} bet={betAmount}");
        _socket.Emit("join_room", new { roomId, playerName, betAmount });
    }

    public void SendShot(float angle, float power)
    {
        if (!IsMyTurn) { Debug.LogWarning("[NetworkManager:SendShot] ❌ Not my turn"); return; }
        _socket.Emit("striker_shot", new { angle, power });
        Debug.Log($"[NetworkManager:SendShot] ✅ angle={angle:F2} power={power:F3}");
    }

    // ✅ NEW: Send real-time positions — called every frame during active shot
    // Uses a plain anonymous object for minimum serialization overhead
    public void SendPositionSync(
        Vector2 strikerPos,
        List<PieceSyncData> pieces)
    {
        // Only the current turn player sends syncs
        if (!IsMyTurn) return;
        _socket.Emit("sync_positions", new
        {
            striker = new { x = strikerPos.x, y = strikerPos.y },
            pieces = pieces
        });
    }

    public void SendShotResult(List<string> pocketedPieceIds, bool strikerPocketed)
    {
        Debug.Log($"[NetworkManager:SendShotResult] pocketed=[{string.Join(",", pocketedPieceIds)}] strikerPocketed={strikerPocketed}");
        _socket.Emit("shot_result", new { pocketedPieces = pocketedPieceIds, strikerPocketed });
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

// ── DATA MODELS ──

[Serializable] public class RoomCreatedData { [JsonProperty("roomId")] public string roomId; [JsonProperty("player")] public PlayerData player; [JsonProperty("betAmount")] public int betAmount; }
[Serializable] public class RoomJoinedData { [JsonProperty("roomId")] public string roomId; [JsonProperty("player")] public PlayerData player; [JsonProperty("betAmount")] public int betAmount; }
[Serializable] public class PlayersData { [JsonProperty("players")] public List<PlayerData> players; }
[Serializable] public class PlayerData { [JsonProperty("id")] public string id; [JsonProperty("name")] public string name; [JsonProperty("coins")] public int coins; [JsonProperty("score")] public int score; [JsonProperty("isHost")] public bool isHost; }
[Serializable] public class GameStartData { [JsonProperty("currentTurn")] public string currentTurn; [JsonProperty("players")] public List<PlayerData> players; [JsonProperty("boardState")] public BoardStateData boardState; [JsonProperty("betAmount")] public int betAmount; [JsonProperty("winScore")] public int winScore; }
[Serializable] public class ShotFiredData { [JsonProperty("playerId")] public string playerId; [JsonProperty("angle")] public float angle; [JsonProperty("power")] public float power; }
[Serializable] public class TurnUpdateData { [JsonProperty("currentTurn")] public string currentTurn; [JsonProperty("scores")] public List<PlayerData> scores; [JsonProperty("boardState")] public BoardStateData boardState; }
[Serializable] public class GameOverData { [JsonProperty("winnerId")] public string winnerId; [JsonProperty("winnerName")] public string winnerName; [JsonProperty("loserName")] public string loserName; [JsonProperty("totalPot")] public int totalPot; [JsonProperty("message")] public string message; }
[Serializable] public class ErrorData { [JsonProperty("message")] public string message; }
[Serializable] public class BoardStateData { [JsonProperty("pieces")] public List<PieceData> pieces; }
[Serializable] public class PieceData { [JsonProperty("id")] public string id; [JsonProperty("type")] public string type; [JsonProperty("x")] public float x; [JsonProperty("y")] public float y; [JsonProperty("active")] public bool active; }

// ✅ NEW: Sync data models
[Serializable]
public class SyncPositionsData
{
    [JsonProperty("striker")] public SyncVec2 striker;
    [JsonProperty("pieces")] public List<PieceSyncData> pieces;
}
[Serializable]
public class SyncVec2
{
    [JsonProperty("x")] public float x;
    [JsonProperty("y")] public float y;
}
[Serializable]
public class PieceSyncData
{
    [JsonProperty("id")] public string id;
    [JsonProperty("x")] public float x;
    [JsonProperty("y")] public float y;
}