using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns pieces based on server board state.
/// Attach to the Board root GameObject.
/// </summary>
public class CarromBoard : MonoBehaviour
{
    [Header("Piece Prefabs")]
    [SerializeField] private GameObject blackPiecePrefab;
    [SerializeField] private GameObject whitePiecePrefab;
    [SerializeField] private GameObject redPiecePrefab;

    [Header("Board Size (world units)")]
    [SerializeField] private float boardSize = 7f; // total width/height of playable area

    private Dictionary<string, GameObject> _spawnedPieces = new Dictionary<string, GameObject>();

    private void Start()
    {
        CarromNetworkManager.Instance.OnGameStart += OnGameStart;
        CarromNetworkManager.Instance.OnTurnUpdate += OnTurnUpdate;
    }

    private void OnGameStart(GameStartData data)
    {
        SpawnPieces(data.boardState);
    }

    private void OnTurnUpdate(TurnUpdateData data)
    {
        // Sync any pieces that should be deactivated
        SyncBoard(data.boardState);
    }

    // ─────────────────────────────────────────
    // SPAWN PIECES FROM SERVER STATE
    // ─────────────────────────────────────────

    public void SpawnPieces(BoardStateData boardState)
    {
        // Clear existing
        foreach (var go in _spawnedPieces.Values)
            if (go) Destroy(go);
        _spawnedPieces.Clear();

        float half = boardSize / 2f;

        foreach (var piece in boardState.pieces)
        {
            if (!piece.active) continue;

            // Convert 0-1 normalized coords → world coords
            float wx = (piece.x - 0.5f) * boardSize;
            float wy = (piece.y - 0.5f) * boardSize;

            GameObject prefab = piece.type switch
            {
                "black" => blackPiecePrefab,
                "white" => whitePiecePrefab,
                "red" => redPiecePrefab,
                _ => blackPiecePrefab
            };

            var go = Instantiate(prefab, new Vector3(wx, wy, 0), Quaternion.identity, transform);
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
            }
            _spawnedPieces[piece.id] = go;
        }
    }

    private void SyncBoard(BoardStateData boardState)
    {
        foreach (var piece in boardState.pieces)
        {
            if (!piece.active && _spawnedPieces.TryGetValue(piece.id, out var go))
            {
                if (go != null) go.SetActive(false);
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