using UnityEngine;

public class PocketTrigger : MonoBehaviour
{
    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
            Debug.LogError($"[PocketTrigger:Awake] ❌ No Collider2D on {gameObject.name}!");
        else if (!col.isTrigger)
            Debug.LogError($"[PocketTrigger:Awake] ❌ Collider2D on {gameObject.name} is NOT a trigger!");
        else
            Debug.Log($"[PocketTrigger:Awake] ✅ Ready: {gameObject.name}");
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"[PocketTrigger:{gameObject.name}] Entered by: {other.gameObject.name} tag={other.tag}");

        // ── PIECE ──
        var piece = other.GetComponent<CarromPiece>();
        if (piece != null)
        {
            Debug.Log($"[PocketTrigger:{gameObject.name}] CarromPiece: id={piece.pieceId} type={piece.pieceType} isPocketed={piece.isPocketed}");
            piece.Pocket();
            return;
        }

        // ── STRIKER ──
        var striker = other.GetComponent<StrikerController>();
        if (striker != null)
        {
            Debug.LogWarning($"[PocketTrigger:{gameObject.name}] ⚠️ STRIKER pocketed!");
            striker.ReportStrikerPocketed();

            // ✅ FIX: Zero velocity via Rigidbody first
            var rb = other.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                // ✅ Freeze briefly so no residual force carries the striker off-screen
                rb.constraints = RigidbodyConstraints2D.FreezeAll;
                Debug.Log("[PocketTrigger] Striker rb frozen");
            }

            // ✅ FIX: Do NOT hardcode position here.
            // StrikerController.ResetStrikerPhysicsAndPosition() will handle correct
            // baseline (host vs guest) when OnTurnUpdate fires from server.
            // Just move off-screen temporarily so it doesn't visually sit in the pocket.
            other.transform.position = new Vector3(100f, 100f, 0f);
            Debug.Log("[PocketTrigger] Striker moved off-screen (100,100,0). Will be reset by OnTurnUpdate.");
            return;
        }

        Debug.LogWarning($"[PocketTrigger:{gameObject.name}] ⚠️ Unknown object: {other.gameObject.name}");
    }
}