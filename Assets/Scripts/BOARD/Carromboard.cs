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
        Debug.Log("[CarromBoard:Start] Subscribing");
        if (CarromNetworkManager.Instance == null) { Debug.LogError("[CarromBoard:Start] ❌ NetworkManager NULL!"); return; }
        if (blackPiecePrefab == null) Debug.LogError("[CarromBoard:Start] ❌ blackPiecePrefab not assigned!");
        if (whitePiecePrefab == null) Debug.LogError("[CarromBoard:Start] ❌ whitePiecePrefab not assigned!");
        if (redPiecePrefab == null) Debug.LogError("[CarromBoard:Start] ❌ redPiecePrefab not assigned!");

        CarromNetworkManager.Instance.OnGameStart += OnGameStart;
        CarromNetworkManager.Instance.OnTurnUpdate += OnTurnUpdate;
        Debug.Log("[CarromBoard:Start] ✅ Subscribed");
    }

    private void OnGameStart(GameStartData data)
    {
        Debug.Log($"[CarromBoard:OnGameStart] pieces={data.boardState?.pieces?.Count}");
        if (data.boardState == null || data.boardState.pieces == null)
        {
            Debug.LogError("[CarromBoard:OnGameStart] ❌ boardState NULL — Newtonsoft required!"); return;
        }
        SpawnPieces(data.boardState);
    }

    private void OnTurnUpdate(TurnUpdateData data)
    {
        Debug.Log($"[CarromBoard:OnTurnUpdate] pieces={data.boardState?.pieces?.Count}");
        if (data.boardState == null) { Debug.LogWarning("[CarromBoard:OnTurnUpdate] boardState NULL"); return; }
        SyncBoard(data.boardState);
    }

    public void SpawnPieces(BoardStateData boardState)
    {
        Debug.Log($"[CarromBoard:SpawnPieces] Clearing {_spawnedPieces.Count} old pieces");
        foreach (var go in _spawnedPieces.Values)
            if (go) Destroy(go);
        _spawnedPieces.Clear();

        int spawned = 0, skipped = 0;
        foreach (var piece in boardState.pieces)
        {
            if (!piece.active) { skipped++; continue; }

            float wx = (piece.x - 0.5f) * boardSize;
            float wy = (piece.y - 0.5f) * boardSize;

            GameObject prefab = piece.type switch
            {
                "white" => whitePiecePrefab,
                "red" => redPiecePrefab,
                _ => blackPiecePrefab
            };
            if (prefab == null) { Debug.LogError($"[CarromBoard:SpawnPieces] ❌ Null prefab for {piece.type}"); continue; }

            var go = Instantiate(prefab, new Vector3(wx, wy, 0), Quaternion.identity, transform);
            var cp = go.GetComponent<CarromPiece>();
            if (cp != null)
            {
                cp.pieceId = piece.id;
                cp.pieceType = piece.type switch { "white" => CarromPiece.PieceType.White, "red" => CarromPiece.PieceType.Red, _ => CarromPiece.PieceType.Black };
                Debug.Log($"[CarromBoard:SpawnPieces] Spawned {piece.id} ({piece.type}) at ({wx:F2},{wy:F2})");
            }
            else
            {
                Debug.LogWarning($"[CarromBoard:SpawnPieces] ⚠️ No CarromPiece on prefab type={piece.type}");
            }

            _spawnedPieces[piece.id] = go;
            spawned++;
        }
        Debug.Log($"[CarromBoard:SpawnPieces] spawned={spawned} skipped={skipped} total={boardState.pieces.Count}");
    }

    private void SyncBoard(BoardStateData boardState)
    {
        int deactivated = 0;
        foreach (var piece in boardState.pieces)
        {
            if (!piece.active && _spawnedPieces.TryGetValue(piece.id, out var go) && go != null && go.activeSelf)
            {
                go.SetActive(false);
                Debug.Log($"[CarromBoard:SyncBoard] Deactivated {piece.id}");
                deactivated++;
            }
        }
        Debug.Log($"[CarromBoard:SyncBoard] Deactivated {deactivated} pieces");
    }

    // ✅ NEW: Called by StrikerController to gather piece positions for broadcast
    // Returns only active pieces (pocketed ones don't need syncing)
    public Dictionary<string, Vector2> GetActivePiecePositions()
    {
        var result = new Dictionary<string, Vector2>();
        foreach (var kv in _spawnedPieces)
        {
            if (kv.Value != null && kv.Value.activeSelf)
                result[kv.Key] = kv.Value.transform.position;
        }
        return result;
    }

    // ✅ NEW: Called by StrikerController when it receives opponent's position sync
    // Moves pieces to the positions the opponent reported
    public void ApplyPiecePositions(List<PieceSyncData> pieces)
    {
        if (pieces == null) return;
        foreach (var sync in pieces)
        {
            if (_spawnedPieces.TryGetValue(sync.id, out var go) && go != null && go.activeSelf)
            {
                var rb = go.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    // ✅ Use MovePosition for kinematic-friendly teleport that respects physics
                    // We keep pieces as dynamic but override position from network
                    rb.MovePosition(new Vector2(sync.x, sync.y));
                }
                else
                {
                    go.transform.position = new Vector3(sync.x, sync.y, 0f);
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (CarromNetworkManager.Instance != null)
        {
            CarromNetworkManager.Instance.OnGameStart -= OnGameStart;
            CarromNetworkManager.Instance.OnTurnUpdate -= OnTurnUpdate;
        }
    }
}