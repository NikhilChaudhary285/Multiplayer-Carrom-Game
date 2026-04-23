using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SocketIOClient;

/// <summary>
/// Core networking manager for Carrom multiplayer.
/// Attach to a persistent GameObject in your scene.
/// Compatible with SocketIOUnity latest versions.
/// </summary>
public class CarromNetworkManager : MonoBehaviour
{
    public static CarromNetworkManager Instance { get; private set; }

    [Header("Server Config")]
    [SerializeField] private string serverUrl = "http://localhost:3000";

    private SocketIOUnity _socket;

    // Events — subscribe from other scripts
    public event Action<RoomCreatedData> OnRoomCreated;
    public event Action<RoomJoinedData> OnRoomJoined;
    public event Action<PlayersData> OnPlayerJoined;
    public event Action<GameStartData> OnGameStart;
    public event Action<ShotFiredData> OnShotFired;
    public event Action<TurnUpdateData> OnTurnUpdate;
    public event Action<GameOverData> OnGameOver;
    public event Action<string> OnError;

    // Local state
    public string MySocketId { get; private set; }
    public string CurrentRoomId { get; private set; }
    public bool IsMyTurn { get; private set; }

    // Thread-safe queue to push socket callbacks onto Unity main thread
    private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start() => Connect();

    // Flush queued callbacks on main thread every frame
    private void Update()
    {
        lock (_mainThreadQueue)
        {
            while (_mainThreadQueue.Count > 0)
                _mainThreadQueue.Dequeue()?.Invoke();
        }
    }

    // Helper — enqueue any action to run on main thread
    private void RunOnMainThread(Action action)
    {
        lock (_mainThreadQueue)
            _mainThreadQueue.Enqueue(action);
    }

    // ─────────────────────────────────────────
    // CONNECTION
    // ─────────────────────────────────────────

    public void Connect()
    {
        var uri = new Uri(serverUrl);
        _socket = new SocketIOUnity(uri, new SocketIOOptions
        {
            Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
            EIO = EngineIO.V4   // ← Fixed: use enum instead of int
        });

        _socket.OnConnected += (sender, e) =>
        {
            MySocketId = _socket.Id;
            Debug.Log($"✅ Connected to server. ID: {MySocketId}");
        };

        _socket.OnDisconnected += (sender, e) =>
        {
            Debug.Log("❌ Disconnected from server");
        };

        RegisterEvents();
        _socket.Connect();
    }

    // ─────────────────────────────────────────
    // REGISTER SOCKET EVENTS
    // ─────────────────────────────────────────

    private void RegisterEvents()
    {
        _socket.On("room_created", response =>
        {
            var data = response.GetValue<RoomCreatedData>();
            CurrentRoomId = data.roomId;
            RunOnMainThread(() => OnRoomCreated?.Invoke(data));
        });

        _socket.On("room_joined", response =>
        {
            var data = response.GetValue<RoomJoinedData>();
            CurrentRoomId = data.roomId;
            RunOnMainThread(() => OnRoomJoined?.Invoke(data));
        });

        _socket.On("player_joined", response =>
        {
            var data = response.GetValue<PlayersData>();
            RunOnMainThread(() => OnPlayerJoined?.Invoke(data));
        });

        _socket.On("game_start", response =>
        {
            var data = response.GetValue<GameStartData>();
            IsMyTurn = data.currentTurn == MySocketId;
            RunOnMainThread(() => OnGameStart?.Invoke(data));
        });

        _socket.On("shot_fired", response =>
        {
            var data = response.GetValue<ShotFiredData>();
            // Only apply opponent's shot (we already applied ours locally)
            if (data.playerId != MySocketId)
                RunOnMainThread(() => OnShotFired?.Invoke(data));
        });

        _socket.On("turn_update", response =>
        {
            var data = response.GetValue<TurnUpdateData>();
            IsMyTurn = data.currentTurn == MySocketId;
            RunOnMainThread(() => OnTurnUpdate?.Invoke(data));
        });

        _socket.On("game_over", response =>
        {
            var data = response.GetValue<GameOverData>();
            RunOnMainThread(() => OnGameOver?.Invoke(data));
        });

        _socket.On("error", response =>
        {
            var msg = response.GetValue<ErrorData>().message;
            RunOnMainThread(() => OnError?.Invoke(msg));
        });
    }

    // ─────────────────────────────────────────
    // EMIT METHODS (called from game scripts)
    // ─────────────────────────────────────────

    public void CreateRoom(string playerName, int betAmount)
    {
        _socket.Emit("create_room", new { playerName, betAmount });
    }

    public void JoinRoom(string roomId, string playerName, int betAmount)
    {
        _socket.Emit("join_room", new { roomId, playerName, betAmount });
    }

    public void SendShot(float angle, float power)
    {
        if (!IsMyTurn) return;
        _socket.Emit("striker_shot", new { angle, power });
    }

    public void SendShotResult(List<string> pocketedPieceIds, bool strikerPocketed)
    {
        _socket.Emit("shot_result", new { pocketedPieces = pocketedPieceIds, strikerPocketed });
    }

    public void RequestSync()
    {
        _socket.Emit("request_sync");
    }

    private void OnDestroy()
    {
        _socket?.Disconnect();
    }
}

// ─────────────────────────────────────────────
// DATA MODELS (match server JSON exactly)
// ─────────────────────────────────────────────

[Serializable] public class RoomCreatedData { public string roomId; public PlayerData player; public int betAmount; }
[Serializable] public class RoomJoinedData { public string roomId; public PlayerData player; public int betAmount; }
[Serializable] public class PlayersData { public List<PlayerData> players; }
[Serializable] public class PlayerData { public string id; public string name; public int score; public bool isHost; }
[Serializable] public class GameStartData { public string currentTurn; public List<PlayerData> players; public BoardStateData boardState; public int betAmount; public int winScore; }
[Serializable] public class ShotFiredData { public string playerId; public float angle; public float power; }
[Serializable] public class TurnUpdateData { public string currentTurn; public List<PlayerData> scores; public BoardStateData boardState; }
[Serializable] public class GameOverData { public string winnerId; public string winnerName; public string loserName; public int totalPot; public string message; }
[Serializable] public class ErrorData { public string message; }
[Serializable] public class BoardStateData { public List<PieceData> pieces; }
[Serializable] public class PieceData { public string id; public string type; public float x; public float y; public bool active; }