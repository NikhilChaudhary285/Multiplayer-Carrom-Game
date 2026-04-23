using System.Collections.Generic;
using UnityEngine;

public class CarromBoard : MonoBehaviour
{
    [Header("Piece Prefabs")]
    [SerializeField] private GameObject blackPiecePrefab;
    [SerializeField] private GameObject whitePiecePrefab;
    [SerializeField] private GameObject redPiecePrefab;

    [Header("Board Size (world units)")]
    [SerializeField] private float boardSize = 7f;

    private Dictionary<string, GameObject> _spawnedPieces = new Dictionary<string, GameObject>();

    private void Start()
    {
        Debug.Log("[CarromBoard:Start] Subscribing to network events");
        if (CarromNetworkManager.Instance == null)
        {
            Debug.LogError("[CarromBoard:Start] ❌ CarromNetworkManager.Instance is NULL!");
            return;
        }
        if (blackPiecePrefab == null) Debug.LogError("[CarromBoard:Start] ❌ blackPiecePrefab not assigned!");
        if (whitePiecePrefab == null) Debug.LogError("[CarromBoard:Start] ❌ whitePiecePrefab not assigned!");
        if (redPiecePrefab == null) Debug.LogError("[CarromBoard:Start] ❌ redPiecePrefab not assigned!");

        CarromNetworkManager.Instance.OnGameStart += OnGameStart;
        CarromNetworkManager.Instance.OnTurnUpdate += OnTurnUpdate;
        Debug.Log("[CarromBoard:Start] Subscribed ✅");
    }

    private void OnGameStart(GameStartData data)
    {
        Debug.Log($"[CarromBoard:OnGameStart] boardState pieces={data.boardState?.pieces?.Count}");
        if (data.boardState == null)
        {
            Debug.LogError("[CarromBoard:OnGameStart] ❌ boardState is NULL in game_start data!");
            return;
        }
        SpawnPieces(data.boardState);
    }

    private void OnTurnUpdate(TurnUpdateData data)
    {
        Debug.Log($"[CarromBoard:OnTurnUpdate] Syncing board from turn_update. pieces={data.boardState?.pieces?.Count}");
        if (data.boardState == null)
        {
            Debug.LogWarning("[CarromBoard:OnTurnUpdate] boardState is NULL — skipping sync");
            return;
        }
        SyncBoard(data.boardState);
    }

    public void SpawnPieces(BoardStateData boardState)
    {
        Debug.Log($"[CarromBoard:SpawnPieces] Clearing {_spawnedPieces.Count} existing pieces");
        foreach (var go in _spawnedPieces.Values)
            if (go) Destroy(go);
        _spawnedPieces.Clear();

        int spawned = 0, skipped = 0;
        foreach (var piece in boardState.pieces)
        {
            if (!piece.active)
            {
                skipped++;
                continue;
            }

            float wx = (piece.x - 0.5f) * boardSize;
            float wy = (piece.y - 0.5f) * boardSize;

            GameObject prefab = piece.type switch
            {
                "black" => blackPiecePrefab,
                "white" => whitePiecePrefab,
                "red" => redPiecePrefab,
                _ => blackPiecePrefab
            };

            if (prefab == null)
            {
                Debug.LogError($"[CarromBoard:SpawnPieces] ❌ Prefab is null for type='{piece.type}' id='{piece.id}'");
                continue;
            }

            var go = Instantiate(prefab, new Vector3(wx, wy, 0), Quaternion.identity, transform);
            Debug.Log($"[CarromBoard:SpawnPieces] Spawned {piece.id} ({piece.type}) at world ({wx:F2}, {wy:F2}) from normalized ({piece.x:F3}, {piece.y:F3})");

            var cp = go.GetComponent<CarromPiece>();
            if (cp != null)
            {
                cp.pieceId = piece.id;
                cp.pieceType = piece.type switch
                {
                    "white" => CarromPiece.PieceType.White,
                    "red" => CarromPiece.PieceType.Red,
                    _ => CarromPiece.PieceType.Black
                };
                Debug.Log($"[CarromBoard:SpawnPieces] CarromPiece configured: id={cp.pieceId} type={cp.pieceType}");
            }
            else
            {
                Debug.LogWarning($"[CarromBoard:SpawnPieces] ⚠️ No CarromPiece component on prefab for type='{piece.type}'");
            }

            _spawnedPieces[piece.id] = go;
            spawned++;
        }

        Debug.Log($"[CarromBoard:SpawnPieces] Done — spawned={spawned} skipped(inactive)={skipped} total={boardState.pieces.Count}");
    }

    private void SyncBoard(BoardStateData boardState)
    {
        int deactivated = 0;
        foreach (var piece in boardState.pieces)
        {
            if (!piece.active && _spawnedPieces.TryGetValue(piece.id, out var go))
            {
                if (go != null && go.activeSelf)
                {
                    go.SetActive(false);
                    Debug.Log($"[CarromBoard:SyncBoard] Deactivated piece: {piece.id}");
                    deactivated++;
                }
            }
        }
        if (deactivated > 0)
            Debug.Log($"[CarromBoard:SyncBoard] Deactivated {deactivated} pieces from server sync");
        else
            Debug.Log("[CarromBoard:SyncBoard] No pieces needed deactivation");
    }

    private void OnDestroy()
    {
        Debug.Log("[CarromBoard:OnDestroy] Cleaning up event subscriptions");
        if (CarromNetworkManager.Instance != null)
        {
            CarromNetworkManager.Instance.OnGameStart -= OnGameStart;
            CarromNetworkManager.Instance.OnTurnUpdate -= OnTurnUpdate;
        }
    }
}