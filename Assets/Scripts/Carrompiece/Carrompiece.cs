using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class CarromPiece : MonoBehaviour
{
    public enum PieceType { Black, White, Red }

    [Header("Piece Config")]
    public PieceType pieceType;
    public string pieceId;

    [HideInInspector] public bool isPocketed = false;

    private Rigidbody2D _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        Debug.Log($"[CarromPiece:Awake] id='{pieceId}' type={pieceType} rb={_rb != null}");
    }

    private void Start()
    {
        // Log after pieceId may have been set by CarromBoard
        Debug.Log($"[CarromPiece:Start] id='{pieceId}' type={pieceType} pos={transform.position}");
        if (string.IsNullOrEmpty(pieceId))
            Debug.LogWarning($"[CarromPiece:Start] ⚠️ pieceId is EMPTY on {gameObject.name} — was it set by CarromBoard?");
    }

    public void Pocket()
    {
        if (isPocketed)
        {
            Debug.LogWarning($"[CarromPiece:Pocket] Pocket() called again on already-pocketed piece: {pieceId}");
            return;
        }
        isPocketed = true;
        Debug.Log($"[CarromPiece:Pocket] 🕳️ Pocketing piece: id={pieceId} type={pieceType}");

        var striker = FindObjectOfType<StrikerController>();
        if (striker != null)
        {
            striker.ReportPiecePocketed(pieceId);
            Debug.Log($"[CarromPiece:Pocket] Reported to StrikerController: {pieceId}");
        }
        else
        {
            Debug.LogError("[CarromPiece:Pocket] ❌ StrikerController not found in scene! Piece pocketed but not reported.");
        }

        gameObject.SetActive(false);
        Debug.Log($"[CarromPiece:Pocket] GameObject deactivated for piece: {pieceId}");
    }
}