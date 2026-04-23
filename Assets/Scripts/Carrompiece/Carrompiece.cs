using UnityEngine;

/// <summary>
/// Attach to every Carrom piece prefab (black, white, red).
/// Handles pocket detection and piece type scoring.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class CarromPiece : MonoBehaviour
{
    public enum PieceType { Black, White, Red }

    [Header("Piece Config")]
    public PieceType pieceType;
    public string pieceId; // Set this to match server IDs (e.g., "black_outer_0")

    [HideInInspector] public bool isPocketed = false;

    private Rigidbody2D _rb;

    private void Awake() => _rb = GetComponent<Rigidbody2D>();

    /// <summary>Called by PocketTrigger when this piece enters a pocket.</summary>
    public void Pocket()
    {
        if (isPocketed) return;
        isPocketed = true;

        // Report to striker controller
        FindObjectOfType<StrikerController>()?.ReportPiecePocketed(pieceId);

        // Hide piece
        gameObject.SetActive(false);

        Debug.Log($"🕳️ Pocketed: {pieceId} ({pieceType})");
    }
}