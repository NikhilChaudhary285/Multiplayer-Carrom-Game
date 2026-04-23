using SocketIOClient;
using System;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Audio.ProcessorInstance;

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
            Debug.LogWarning("[NetworkManager:Awake] Duplicate instance found — destroying this one");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[NetworkManager:Awake] Singleton set, DontDestroyOnLoad applied");
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
            {
                var action = _mainThreadQueue.Dequeue();
                action?.Invoke();
            }
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
        Debug.Log($"[NetworkManager:Connect] Attempting connection to: {serverUrl}");
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
            Debug.LogWarning($"[NetworkManager:OnDisconnected] ❌ Disconnected from server. Reason: {e}");
        };

        _socket.OnError += (sender, e) =>
        {
            Debug.LogError($"[NetworkManager:OnError] Socket error: {e}");
        };

        _socket.OnReconnected += (sender, e) =>
        {
            Debug.Log($"[NetworkManager:OnReconnected] Reconnected after {e} attempts");
        };

        RegisterEvents();

        Debug.Log("[NetworkManager:Connect] Registered all events — calling _socket.Connect()");
        _socket.Connect();
    }

    // ── REGISTER SOCKET EVENTS ──

    private void RegisterEvents()
    {
        Debug.Log("[NetworkManager:RegisterEvents] Registering all socket event listeners");

        _socket.On("room_created", response =>
        {
            var json = response.ToString().Trim('[', ']');
            Debug.Log($"[NetworkManager:room_created] Raw response: {json}");
            try
            {
                var data = JsonUtility.FromJson<RoomCreatedData>(json);
                CurrentRoomId = data.roomId;
                Debug.Log($"[NetworkManager:room_created] Parsed — roomId={data.roomId} betAmount={data.betAmount} playerId={data.player?.id}");
                RunOnMainThread(() => OnRoomCreated?.Invoke(data));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager:room_created] Parse error: {ex.Message}");
            }
        });

        _socket.On("room_joined", response =>
        {
            var json = response.ToString().Trim('[', ']');
            Debug.Log($"[NetworkManager:room_joined] Raw response: {response}");
            try
            {
                var data = JsonUtility.FromJson<RoomJoinedData>(json);
                CurrentRoomId = data.roomId;
                Debug.Log($"[NetworkManager:room_joined] Parsed — roomId={data.roomId} betAmount={data.betAmount}");
                RunOnMainThread(() => OnRoomJoined?.Invoke(data));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager:room_joined] Parse error: {ex.Message}");
            }
        });

        _socket.On("player_joined", response =>
        {
            var json = response.ToString().Trim('[', ']');
            Debug.Log($"[NetworkManager:player_joined] Raw response: {response}");
            try
            {
                var data = JsonUtility.FromJson<PlayersData>(json);
                Debug.Log($"[NetworkManager:player_joined] Players in room: {data.players?.Count}");
                RunOnMainThread(() => OnPlayerJoined?.Invoke(data));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager:player_joined] Parse error: {ex.Message}");
            }
        });

        _socket.On("game_start", response =>
        {
            var json = response.ToString().Trim('[', ']');
            Debug.Log($"[NetworkManager:game_start] Raw response: {response}");
            try
            {
                var data = JsonUtility.FromJson<GameStartData>(json);
                IsMyTurn = data.currentTurn == MySocketId;
                Debug.Log($"[NetworkManager:game_start] currentTurn={data.currentTurn} MySocketId={MySocketId} IsMyTurn={IsMyTurn} winScore={data.winScore} betAmount={data.betAmount}");
                Debug.Log($"[NetworkManager:game_start] Board pieces count: {data.boardState?.pieces?.Count}");
                RunOnMainThread(() => OnGameStart?.Invoke(data));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager:game_start] Parse error: {ex.Message}");
            }
        });

        _socket.On("shot_fired", response =>
        {
            var json = response.ToString().Trim('[', ']');
            Debug.Log($"[NetworkManager:shot_fired] Raw response: {response}");
            try
            {
                var data = JsonUtility.FromJson<ShotFiredData>(json);
                Debug.Log($"[NetworkManager:shot_fired] playerId={data.playerId} angle={data.angle} power={data.power} isOpponent={data.playerId != MySocketId}");
                if (data.playerId != MySocketId)
                {
                    Debug.Log("[NetworkManager:shot_fired] Dispatching opponent shot to main thread");
                    RunOnMainThread(() => OnShotFired?.Invoke(data));
                }
                else
                {
                    Debug.Log("[NetworkManager:shot_fired] Shot is mine — skipping (already applied locally)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager:shot_fired] Parse error: {ex.Message}");
            }
        });

        _socket.On("turn_update", response =>
        {
            var json = response.ToString().Trim('[', ']');
            Debug.Log($"[NetworkManager:turn_update] Raw response: {response}");
            try
            {
                var data = JsonUtility.FromJson<TurnUpdateData>(json);
                IsMyTurn = data.currentTurn == MySocketId;
                Debug.Log($"[NetworkManager:turn_update] currentTurn={data.currentTurn} IsMyTurn={IsMyTurn}");
                if (data.scores != null)
                    foreach (var s in data.scores)
                        Debug.Log($"[NetworkManager:turn_update] Score — {s.name}: {s.score}");
                RunOnMainThread(() => OnTurnUpdate?.Invoke(data));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager:turn_update] Parse error: {ex.Message}");
            }
        });

        _socket.On("game_over", response =>
        {
            var json = response.ToString().Trim('[', ']');
            Debug.Log($"[NetworkManager:game_over] Raw response: {response}");
            try
            {
                var data = JsonUtility.FromJson<GameOverData>(json);
                Debug.Log($"[NetworkManager:game_over] winnerId={data.winnerId} winnerName={data.winnerName} totalPot={data.totalPot} message={data.message}");
                RunOnMainThread(() => OnGameOver?.Invoke(data));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager:game_over] Parse error: {ex.Message}");
            }
        });

        _socket.On("board_sync", response =>
        {
            var json = response.ToString().Trim('[', ']');
            Debug.Log($"[NetworkManager:board_sync] Received board sync");
        });

        _socket.On("error", response =>
        {
            var json = response.ToString().Trim('[', ']');
            Debug.LogError($"[NetworkManager:error] Raw: {response}");
            try
            {
                var data = JsonUtility.FromJson<ErrorData>(json);
                Debug.LogError($"[NetworkManager:error] Server error message: {data.message}");
                RunOnMainThread(() => OnError?.Invoke(data.message));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager:error] Parse error: {ex.Message}");
            }
        });

        Debug.Log("[NetworkManager:RegisterEvents] All events registered ✅");
    }

    // ── EMIT METHODS ──

    public void CreateRoom(string playerName, int betAmount)
    {
        Debug.Log($"[NetworkManager:CreateRoom] Emitting create_room — playerName={playerName} betAmount={betAmount}");
        _socket.Emit("create_room", new { playerName, betAmount });
    }

    public void JoinRoom(string roomId, string playerName, int betAmount)
    {
        Debug.Log($"[NetworkManager:JoinRoom] Emitting join_room — roomId={roomId} playerName={playerName} betAmount={betAmount}");
        _socket.Emit("join_room", new { roomId, playerName, betAmount });
    }

    public void SendShot(float angle, float power)
    {
        Debug.Log($"[NetworkManager:SendShot] IsMyTurn={IsMyTurn} angle={angle} power={power}");
        if (!IsMyTurn)
        {
            Debug.LogWarning("[NetworkManager:SendShot] Blocked — not my turn!");
            return;
        }
        _socket.Emit("striker_shot", new { angle, power });
        Debug.Log("[NetworkManager:SendShot] striker_shot emitted ✅");
    }

    public void SendShotResult(List<string> pocketedPieceIds, bool strikerPocketed)
    {
        Debug.Log($"[NetworkManager:SendShotResult] pocketedPieces={string.Join(",", pocketedPieceIds)} strikerPocketed={strikerPocketed}");
        _socket.Emit("shot_result", new { pocketedPieces = pocketedPieceIds, strikerPocketed });
        Debug.Log("[NetworkManager:SendShotResult] shot_result emitted ✅");
    }

    public void RequestSync()
    {
        Debug.Log("[NetworkManager:RequestSync] Emitting request_sync");
        _socket.Emit("request_sync");
    }

    private void OnDestroy()
    {
        Debug.Log("[NetworkManager:OnDestroy] Disconnecting socket");
        _socket?.Disconnect();
    }
}

[Serializable] public class RoomCreatedData { public string roomId; public PlayerData player; public int betAmount; }
[Serializable] public class RoomJoinedData { public string roomId; public PlayerData player; public int betAmount; }
[Serializable] public class PlayersData { public List<PlayerData> players; }
[Serializable] public class PlayerData { public string id; public string name; public int coins; public int score; public bool isHost; }
[Serializable] public class GameStartData { public string currentTurn; public List<PlayerData> players; public BoardStateData boardState; public int betAmount; public int winScore; }
[Serializable] public class ShotFiredData { public string playerId; public float angle; public float power; }
[Serializable] public class TurnUpdateData { public string currentTurn; public List<PlayerData> scores; public BoardStateData boardState; }
[Serializable] public class GameOverData { public string winnerId; public string winnerName; public string loserName; public int totalPot; public string message; }
[Serializable] public class ErrorData { public string message; }
[Serializable] public class BoardStateData { public List<PieceData> pieces; }
[Serializable] public class PieceData { public string id; public string type; public float x; public float y; public bool active; }