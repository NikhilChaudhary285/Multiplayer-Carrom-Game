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
        // pieceId is not yet set here (set by CarromBoard after Instantiate)
        Debug.Log($"[CarromPiece:Awake] type={pieceType} go={gameObject.name}");
    }

    private void Start()
    {
        Debug.Log($"[CarromPiece:Start] id='{pieceId}' type={pieceType} pos={transform.position}");
        if (string.IsNullOrEmpty(pieceId))
            Debug.LogWarning($"[CarromPiece:Start] ⚠️ pieceId EMPTY on {gameObject.name} — CarromBoard may not have set it yet");
    }

    public void Pocket()
    {
        if (isPocketed)
        {
            Debug.LogWarning($"[CarromPiece:Pocket] Already pocketed: {pieceId} — ignoring duplicate call");
            return;
        }

        isPocketed = true;
        Debug.Log($"[CarromPiece:Pocket] 🕳️ id={pieceId} type={pieceType}");

        // ✅ FIX: Use FindObjectsByType (non-deprecated) instead of FindObjectOfType
        var striker = FindObjectsByType<StrikerController>(FindObjectsSortMode.None);
        if (striker != null && striker.Length > 0)
        {
            striker[0].ReportPiecePocketed(pieceId);
            Debug.Log($"[CarromPiece:Pocket] Reported to StrikerController");
        }
        else
        {
            Debug.LogError($"[CarromPiece:Pocket] ❌ No StrikerController in scene! Piece {pieceId} pocketed but not reported.");
        }

        gameObject.SetActive(false);
        Debug.Log($"[CarromPiece:Pocket] GameObject disabled: {pieceId}");
    }
}