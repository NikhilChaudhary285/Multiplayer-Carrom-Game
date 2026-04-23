using UnityEngine;

/// <summary>
/// Attach to each of the 4 pocket trigger zones (small circles in board corners).
/// Uses OnTriggerEnter2D to detect pieces falling in.
/// </summary>
public class PocketTrigger : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        // Check if it's a piece
        CarromPiece piece = other.GetComponent<CarromPiece>();
        if (piece != null)
        {
            piece.Pocket();
            return;
        }

        // Check if it's the striker
        StrikerController striker = other.GetComponent<StrikerController>();
        if (striker != null)
        {
            striker.ReportStrikerPocketed();
            // Reset striker position
            other.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
            other.transform.position = new Vector3(0, -3.5f, 0); // back to baseline
        }
    }
}