using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles striker drag-aim-flick mechanic.
/// Attach to the Striker GameObject.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class StrikerController : MonoBehaviour
{
    [Header("Shot Settings")]
    [SerializeField] private float maxPower = 800f;
    [SerializeField] private float maxDragDistance = 2f;
    [SerializeField] private LineRenderer aimLine;  // drag in Inspector
    [SerializeField] private Transform strikerSlideArea; // horizontal slide zone

    private Rigidbody2D _rb;
    private Vector2 _dragStart;
    private bool _isDragging = false;
    private bool _shotFired = false;
    private bool _canShoot = false;

    // Pocketed tracking this turn
    private List<string> _pocketedThisTurn = new List<string>();
    private bool _strikerPocketed = false;

    // Board reference
    [SerializeField] private CarromBoard boardRef;

    private void Awake() => _rb = GetComponent<Rigidbody2D>();

    private void Start()
    {
        // Subscribe to network events
        CarromNetworkManager.Instance.OnGameStart += OnGameStart;
        CarromNetworkManager.Instance.OnShotFired += OnOpponentShot;
        CarromNetworkManager.Instance.OnTurnUpdate += OnTurnUpdate;
    }

    private void OnGameStart(GameStartData data)
    {
        _canShoot = data.currentTurn == CarromNetworkManager.Instance.MySocketId;
        ResetStriker();
    }

    private void OnTurnUpdate(TurnUpdateData data)
    {
        _canShoot = data.currentTurn == CarromNetworkManager.Instance.MySocketId;
        _shotFired = false;
        _pocketedThisTurn.Clear();
        _strikerPocketed = false;
        ResetStriker();
    }

    // ─────────────────────────────────────────
    // INPUT HANDLING
    // ─────────────────────────────────────────

    private void Update()
    {
        if (!_canShoot || _shotFired) return;

        if (Input.GetMouseButtonDown(0)) OnDragStart();
        else if (Input.GetMouseButton(0) && _isDragging) OnDragUpdate();
        else if (Input.GetMouseButtonUp(0) && _isDragging) OnDragRelease();
    }

    private void OnDragStart()
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        // Only start drag if clicking near striker
        if (Vector2.Distance(mousePos, transform.position) < 0.5f)
        {
            _dragStart = mousePos;
            _isDragging = true;
        }
    }

    private void OnDragUpdate()
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dragDir = _dragStart - mousePos;
        float dragDist = Mathf.Clamp(dragDir.magnitude, 0, maxDragDistance);

        // Show aim line
        if (aimLine != null)
        {
            aimLine.enabled = true;
            aimLine.SetPosition(0, transform.position);
            aimLine.SetPosition(1, transform.position + (Vector3)(dragDir.normalized * dragDist * 1.5f));
        }
    }

    private void OnDragRelease()
    {
        _isDragging = false;
        if (aimLine != null) aimLine.enabled = false;

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dragDir = _dragStart - mousePos;
        float dragDist = Mathf.Clamp(dragDir.magnitude, 0, maxDragDistance);

        if (dragDist < 0.05f) return; // too small, ignore

        float power = dragDist / maxDragDistance;           // 0–1
        float angle = Mathf.Atan2(dragDir.y, dragDir.x) * Mathf.Rad2Deg; // degrees

        FireStriker(angle, power);
    }

    // ─────────────────────────────────────────
    // FIRE SHOT
    // ─────────────────────────────────────────

    private void FireStriker(float angle, float power)
    {
        _shotFired = true;
        _canShoot = false;

        Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        _rb.AddForce(direction * power * maxPower);

        // Send to server
        CarromNetworkManager.Instance.SendShot(angle, power);

        // Start watching for pieces settling
        StartCoroutine(WaitForShotSettle());
    }

    // ─────────────────────────────────────────
    // OPPONENT SHOT (received from server)
    // ─────────────────────────────────────────

    private void OnOpponentShot(ShotFiredData data)
    {
        Vector2 direction = new Vector2(
            Mathf.Cos(data.angle * Mathf.Deg2Rad),
            Mathf.Sin(data.angle * Mathf.Deg2Rad)
        );
        _rb.AddForce(direction * data.power * maxPower);
        StartCoroutine(WaitForShotSettle());
    }

    // ─────────────────────────────────────────
    // WAIT FOR ALL PHYSICS TO SETTLE
    // ─────────────────────────────────────────

    private IEnumerator WaitForShotSettle()
    {
        yield return new WaitForSeconds(0.5f); // small delay

        float settleTimer = 0f;
        float settleTimeout = 5f;

        while (settleTimer < settleTimeout)
        {
            if (AllPiecesSettled())
                break;
            settleTimer += Time.deltaTime;
            yield return null;
        }

        // Report result to server (only the current turn player reports)
        if (CarromNetworkManager.Instance.MySocketId != null)
        {
            // Only host-side reports to avoid double-reporting
            // In real game: both clients calculate, server trusts one
            CarromNetworkManager.Instance.SendShotResult(_pocketedThisTurn, _strikerPocketed);
        }
    }

    private bool AllPiecesSettled()
    {
        // Check if all rigidbodies on board have near-zero velocity
        //var pieces = FindObjectsOfType<CarromPiece>();
        var pieces = FindObjectsByType<CarromPiece>(FindObjectsSortMode.None);
        foreach (var p in pieces)
        {
            if (p.GetComponent<Rigidbody2D>().linearVelocity.magnitude > 0.05f)
                return false;
        }
        return _rb.linearVelocity.magnitude < 0.05f;
    }

    // ─────────────────────────────────────────
    // POCKET DETECTION (called by PocketTrigger)
    // ─────────────────────────────────────────

    public void ReportPiecePocketed(string pieceId)
    {
        _pocketedThisTurn.Add(pieceId);
    }

    public void ReportStrikerPocketed()
    {
        _strikerPocketed = true;
    }

    private void ResetStriker()
    {
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        // Reposition striker to player's baseline
        // Player 1 (host): bottom baseline. Player 2: top baseline
        bool isHost = false; // set from game start data
        transform.position = isHost ? new Vector3(0, -3.5f, 0) : new Vector3(0, 3.5f, 0);
    }

    private void OnDestroy()
    {
        if (CarromNetworkManager.Instance != null)
        {
            CarromNetworkManager.Instance.OnGameStart -= OnGameStart;
            CarromNetworkManager.Instance.OnShotFired -= OnOpponentShot;
            CarromNetworkManager.Instance.OnTurnUpdate -= OnTurnUpdate;
        }
    }
}