using UnityEngine;

public class PocketTrigger : MonoBehaviour
{
    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
            Debug.LogError($"[PocketTrigger:Awake] ❌ No Collider2D on {gameObject.name}! Pocket detection will not work.");
        else if (!col.isTrigger)
            Debug.LogError($"[PocketTrigger:Awake] ❌ Collider2D on {gameObject.name} is NOT set to IsTrigger! Enable it in Inspector.");
        else
            Debug.Log($"[PocketTrigger:Awake] ✅ Pocket trigger ready on {gameObject.name}");
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"[PocketTrigger:OnTriggerEnter2D] Object entered pocket '{gameObject.name}': {other.gameObject.name} (tag={other.tag})");

        CarromPiece piece = other.GetComponent<CarromPiece>();
        if (piece != null)
        {
            Debug.Log($"[PocketTrigger:OnTriggerEnter2D] ✅ CarromPiece detected: id={piece.pieceId} type={piece.pieceType} isPocketed={piece.isPocketed}");
            piece.Pocket();
            return;
        }

        StrikerController striker = other.GetComponent<StrikerController>();
        if (striker != null)
        {
            Debug.LogWarning($"[PocketTrigger:OnTriggerEnter2D] ⚠️ STRIKER entered pocket '{gameObject.name}'!");
            striker.ReportStrikerPocketed();
            var rb = other.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                Debug.Log("[PocketTrigger:OnTriggerEnter2D] Striker velocity zeroed");
            }
            other.transform.position = new Vector3(0, -3.5f, 0);
            Debug.Log("[PocketTrigger:OnTriggerEnter2D] Striker repositioned to baseline (0, -3.5, 0)");
            return;
        }

        Debug.LogWarning($"[PocketTrigger:OnTriggerEnter2D] ⚠️ Unknown object entered pocket: {other.gameObject.name} — no CarromPiece or StrikerController found");
    }
}