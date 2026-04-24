using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class StrikerController : MonoBehaviour
{
    [Header("Shot Settings")]
    [SerializeField] private float maxPower = 800f;
    [SerializeField] private float maxDragDistance = 2f;
    [SerializeField] private LineRenderer aimLine;
    [SerializeField] private float dragClickRadius = 0.4f;

    [Header("Striker Baseline Positions")]
    [SerializeField] private float hostBaselineY = -3f;
    [SerializeField] private float guestBaselineY = 3f;
    [SerializeField] private float strikerBaselineX = 0f;

    [Header("Sync Settings")]
    // ✅ NEW: How many times per second to send position sync
    // 20 is smooth enough without flooding the server
    [SerializeField] private float syncRatePerSecond = 20f;

    private Rigidbody2D _rb;
    private Vector2 _dragStart;
    private bool _isDragging = false;
    private bool _dragInitiatedOnStriker = false;
    private bool _shotFired = false;
    private bool _canShoot = false;
    private bool _isHost = false;
    private bool _iAmCurrentShooter = false;
    private bool _inputBlocked = false;
    private float _inputBlockTimer = 0f;
    private const float INPUT_BLOCK_DURATION = 0.3f;
    private Coroutine _settleCoroutine = null;
    private List<string> _pocketedThisTurn = new List<string>();
    private bool _strikerPocketed = false;
    private string _mySocketId = "";

    // ✅ NEW: Sync state
    // _isSyncing = true while the shot is in flight and we should broadcast
    private bool _isSyncing = false;
    private float _syncTimer = 0f;

    // ✅ NEW: Reference to CarromBoard so we can read piece positions for sync
    // Assign in Inspector OR auto-find in Start
    [SerializeField] private CarromBoard boardRef;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        if (_rb == null) Debug.LogError("[StrikerController:Awake] ❌ Rigidbody2D missing!");
        if (aimLine == null) Debug.LogWarning("[StrikerController:Awake] ⚠️ aimLine not assigned");
        Debug.Log($"[StrikerController:Awake] maxPower={maxPower} dragRadius={dragClickRadius} syncRate={syncRatePerSecond}");
    }

    private void Start()
    {
        // Auto-find board if not assigned
        if (boardRef == null)
        {
            boardRef = FindObjectOfType<CarromBoard>();
            if (boardRef == null)
                Debug.LogError("[StrikerController:Start] ❌ CarromBoard not found in scene! Sync will not include pieces.");
            else
                Debug.Log("[StrikerController:Start] CarromBoard auto-found");
        }

        if (CarromNetworkManager.Instance == null)
        {
            Debug.LogError("[StrikerController:Start] ❌ CarromNetworkManager.Instance NULL!");
            return;
        }

        CarromNetworkManager.Instance.OnGameStart += OnGameStart;
        CarromNetworkManager.Instance.OnShotFired += OnOpponentShot;
        CarromNetworkManager.Instance.OnTurnUpdate += OnTurnUpdate;
        // ✅ NEW: Subscribe to incoming position syncs from opponent
        CarromNetworkManager.Instance.OnSyncPositions += OnReceiveSyncPositions;

        _canShoot = false;
        _inputBlocked = false;
        Debug.Log("[StrikerController:Start] ✅ Subscribed. Waiting for game_start.");
    }

    private void Update()
    {
        // ── INPUT BLOCK ──
        if (_inputBlocked)
        {
            _inputBlockTimer -= Time.deltaTime;
            if (_inputBlockTimer <= 0f)
            {
                _inputBlocked = false;
                Debug.Log("[StrikerController:Update] ✅ Input unblocked");
            }
            return;
        }

        // ── POSITION BROADCAST (shooter only) ──
        // ✅ NEW: While our shot is in flight, broadcast positions at syncRatePerSecond
        if (_isSyncing && _iAmCurrentShooter)
        {
            _syncTimer += Time.deltaTime;
            float interval = 1f / syncRatePerSecond;
            if (_syncTimer >= interval)
            {
                _syncTimer = 0f;
                BroadcastPositions();
            }
        }

        if (!_canShoot || _shotFired) return;

        if (Input.GetMouseButtonDown(0))
            OnDragStart();
        else if (Input.GetMouseButton(0) && _isDragging && _dragInitiatedOnStriker)
            OnDragUpdate();
        else if (Input.GetMouseButtonUp(0))
        {
            if (_isDragging && _dragInitiatedOnStriker)
                OnDragRelease();
            else
            {
                _isDragging = false;
                _dragInitiatedOnStriker = false;
                if (aimLine != null) aimLine.enabled = false;
            }
        }
    }

    // ─────────────────────────────────────────
    // POSITION BROADCAST (my shot → opponent sees it)
    // ─────────────────────────────────────────

    // ✅ NEW: Gather current world positions and send to server for relay
    private void BroadcastPositions()
    {
        // Build piece position list from CarromBoard's spawned pieces
        var pieceSyncs = new List<PieceSyncData>();
        if (boardRef != null)
        {
            var pieceObjects = boardRef.GetActivePiecePositions();
            foreach (var kv in pieceObjects)
            {
                pieceSyncs.Add(new PieceSyncData
                {
                    id = kv.Key,
                    x = kv.Value.x,
                    y = kv.Value.y
                });
            }
        }

        CarromNetworkManager.Instance.SendPositionSync(
            (Vector2)transform.position,
            pieceSyncs
        );
    }

    // ─────────────────────────────────────────
    // RECEIVE OPPONENT POSITIONS
    // ─────────────────────────────────────────

    // ✅ NEW: Apply opponent's broadcast positions to our local objects
    private void OnReceiveSyncPositions(SyncPositionsData data)
    {
        // Move our striker to opponent's striker position
        if (data.striker != null)
        {
            Vector3 targetPos = new Vector3(data.striker.x, data.striker.y, 0f);
            // ✅ Teleport — no smoothing needed at 20Hz, physics handles the look
            transform.position = targetPos;
        }

        // Move pieces to opponent's reported positions
        if (data.pieces != null && boardRef != null)
        {
            boardRef.ApplyPiecePositions(data.pieces);
        }
    }

    // ─────────────────────────────────────────
    // NETWORK CALLBACKS
    // ─────────────────────────────────────────

    private void OnGameStart(GameStartData data)
    {
        _mySocketId = CarromNetworkManager.Instance.MySocketId;
        if (string.IsNullOrEmpty(_mySocketId))
            Debug.LogError("[StrikerController:OnGameStart] ❌ MySocketId EMPTY!");

        _canShoot = data.currentTurn == _mySocketId;
        _isHost = false;

        if (data.players != null)
            foreach (var p in data.players)
            {
                Debug.Log($"[StrikerController:OnGameStart] Player: id={p.id} name={p.name} isHost={p.isHost}");
                if (p.id == _mySocketId) { _isHost = p.isHost; break; }
            }

        Debug.Log($"[StrikerController:OnGameStart] myId={_mySocketId} _canShoot={_canShoot} _isHost={_isHost}");

        StopSettleCoroutine();
        StopSyncing();
        ResetTurnState();
        ResetStrikerPhysicsAndPosition();
        BlockInputBriefly();
    }

    private void OnTurnUpdate(TurnUpdateData data)
    {
        if (string.IsNullOrEmpty(_mySocketId))
        {
            _mySocketId = CarromNetworkManager.Instance.MySocketId;
            Debug.LogWarning($"[StrikerController:OnTurnUpdate] Re-cached myId={_mySocketId}");
        }

        bool prev = _canShoot;
        _canShoot = data.currentTurn == _mySocketId;
        Debug.Log($"[StrikerController:OnTurnUpdate] currentTurn={data.currentTurn} prev={prev} → _canShoot={_canShoot}");

        StopSettleCoroutine();
        StopSyncing();
        ResetTurnState();
        ResetStrikerPhysicsAndPosition();
        BlockInputBriefly();
    }

    // ─────────────────────────────────────────
    // RESET HELPERS
    // ─────────────────────────────────────────

    private void ResetTurnState()
    {
        _isDragging = false;
        _dragInitiatedOnStriker = false;
        _shotFired = false;
        _iAmCurrentShooter = false;
        _pocketedThisTurn.Clear();
        _strikerPocketed = false;
        if (aimLine != null) aimLine.enabled = false;
        Debug.Log("[StrikerController:ResetTurnState] ✅ Cleared");
    }

    private void ResetStrikerPhysicsAndPosition()
    {
        _rb.constraints = RigidbodyConstraints2D.FreezeAll;
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        float y = _isHost ? hostBaselineY : guestBaselineY;
        transform.position = new Vector3(strikerBaselineX, y, 0f);
        Debug.Log($"[StrikerController:ResetStrikerPhysicsAndPosition] pos=(0,{y}) isHost={_isHost}");
        StartCoroutine(UnfreezeNextFrame());
    }

    private IEnumerator UnfreezeNextFrame()
    {
        yield return null;
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        Debug.Log("[StrikerController:UnfreezeNextFrame] ✅ Unfrozen");
    }

    private void BlockInputBriefly()
    {
        _inputBlocked = true;
        _inputBlockTimer = INPUT_BLOCK_DURATION;
        Debug.Log($"[StrikerController:BlockInputBriefly] Blocked {INPUT_BLOCK_DURATION}s");
    }

    private void StopSettleCoroutine()
    {
        if (_settleCoroutine != null)
        {
            StopCoroutine(_settleCoroutine);
            _settleCoroutine = null;
            Debug.Log("[StrikerController:StopSettleCoroutine] ✅ Stopped");
        }
    }

    // ✅ NEW: Stop position broadcasting
    private void StopSyncing()
    {
        if (_isSyncing)
        {
            _isSyncing = false;
            _syncTimer = 0f;
            Debug.Log("[StrikerController:StopSyncing] ✅ Sync stopped");
        }
    }

    // ─────────────────────────────────────────
    // DRAG INPUT
    // ─────────────────────────────────────────

    private void OnDragStart()
    {
        if (Camera.main == null) { Debug.LogError("[StrikerController:OnDragStart] ❌ Camera.main NULL"); return; }
        Vector2 mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        float dist = Vector2.Distance(mouse, transform.position);
        Debug.Log($"[StrikerController:OnDragStart] dist={dist:F3} threshold={dragClickRadius}");

        if (dist <= dragClickRadius)
        {
            _dragStart = mouse; _isDragging = true; _dragInitiatedOnStriker = true;
            Debug.Log("[StrikerController:OnDragStart] ✅ Drag started");
        }
        else
        {
            _isDragging = false; _dragInitiatedOnStriker = false;
            Debug.Log($"[StrikerController:OnDragStart] ❌ Too far ({dist:F3}) — ignored");
        }
    }

    private void OnDragUpdate()
    {
        if (Camera.main == null) return;
        Vector2 mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dragDir = _dragStart - mouse;
        float dist = Mathf.Clamp(dragDir.magnitude, 0, maxDragDistance);
        if (aimLine != null)
        {
            aimLine.enabled = true;
            aimLine.SetPosition(0, transform.position);
            aimLine.SetPosition(1, transform.position + (Vector3)(dragDir.normalized * dist * 1.5f));
        }
    }

    private void OnDragRelease()
    {
        _isDragging = false; _dragInitiatedOnStriker = false;
        if (aimLine != null) aimLine.enabled = false;
        if (Camera.main == null) return;

        Vector2 mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dragDir = _dragStart - mouse;
        float dist = Mathf.Clamp(dragDir.magnitude, 0, maxDragDistance);
        Debug.Log($"[StrikerController:OnDragRelease] dist={dist:F3}");

        if (dist < 0.05f) { Debug.LogWarning("[StrikerController:OnDragRelease] Too small — ignored"); return; }

        float power = dist / maxDragDistance;
        float angle = Mathf.Atan2(dragDir.y, dragDir.x) * Mathf.Rad2Deg;
        Debug.Log($"[StrikerController:OnDragRelease] power={power:F3} angle={angle:F2}°");
        FireStriker(angle, power);
    }

    // ─────────────────────────────────────────
    // FIRE
    // ─────────────────────────────────────────

    private void FireStriker(float angle, float power)
    {
        if (!_canShoot || _shotFired || _inputBlocked) { Debug.LogWarning("[StrikerController:FireStriker] ❌ Blocked"); return; }

        _shotFired = true;
        _canShoot = false;
        _iAmCurrentShooter = true;
        _isSyncing = true;  // ✅ NEW: Start broadcasting positions
        _syncTimer = 0f;

        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        _rb.AddForce(dir * power * maxPower);

        Debug.Log($"[StrikerController:FireStriker] ✅ Fired angle={angle:F2} power={power:F3} sync=ON");
        CarromNetworkManager.Instance.SendShot(angle, power);

        StopSettleCoroutine();
        _settleCoroutine = StartCoroutine(WaitForShotSettle());
    }

    // ─────────────────────────────────────────
    // OPPONENT SHOT — physics only
    // ─────────────────────────────────────────

    private void OnOpponentShot(ShotFiredData data)
    {
        if (data.playerId == _mySocketId) { Debug.LogWarning("[StrikerController:OnOpponentShot] My own id — skip"); return; }
        Debug.Log($"[StrikerController:OnOpponentShot] angle={data.angle} power={data.power}");

        // ✅ When opponent fires, unfreeze our striker so it can receive sync positions
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        // NOTE: We do NOT apply AddForce here anymore.
        // Position is driven entirely by OnReceiveSyncPositions.
        // This avoids double-physics (local sim + remote sim diverging).
        Debug.Log("[StrikerController:OnOpponentShot] ✅ Striker ready to receive sync positions.");
    }

    // ─────────────────────────────────────────
    // SETTLE COROUTINE
    // ─────────────────────────────────────────

    private IEnumerator WaitForShotSettle()
    {
        if (!_iAmCurrentShooter) { Debug.LogError("[StrikerController:WaitForShotSettle] ❌ Not shooter — abort"); yield break; }

        yield return new WaitForSeconds(0.5f);

        float timer = 0f, timeout = 6f;
        while (timer < timeout)
        {
            if (AllPiecesSettled()) break;
            timer += Time.deltaTime;
            yield return null;
        }

        if (timer >= timeout) Debug.LogWarning("[StrikerController:WaitForShotSettle] ⚠️ Timeout");
        else Debug.Log($"[StrikerController:WaitForShotSettle] ✅ Settled in {timer:F2}s");

        // ✅ Stop syncing once settled — no need to keep sending static positions
        StopSyncing();
        // Send one final sync so opponent's board matches exactly
        BroadcastPositions();

        Debug.Log($"[StrikerController:WaitForShotSettle] Reporting pocketed=[{string.Join(",", _pocketedThisTurn)}] striker={_strikerPocketed}");
        _settleCoroutine = null;
        CarromNetworkManager.Instance.SendShotResult(_pocketedThisTurn, _strikerPocketed);
    }

    private bool AllPiecesSettled()
    {
        var pieces = FindObjectsByType<CarromPiece>(FindObjectsSortMode.None);
        foreach (var p in pieces)
        {
            var rb = p.GetComponent<Rigidbody2D>();
            if (rb != null && rb.linearVelocity.magnitude > 0.05f) return false;
        }
        return _rb.linearVelocity.magnitude < 0.05f;
    }

    // ─────────────────────────────────────────
    // POCKET REPORTS
    // ─────────────────────────────────────────

    public void ReportPiecePocketed(string pieceId)
    {
        Debug.Log($"[StrikerController:ReportPiecePocketed] {pieceId} _iAmCurrentShooter={_iAmCurrentShooter}");
        if (_iAmCurrentShooter) _pocketedThisTurn.Add(pieceId);
        else Debug.LogWarning($"[StrikerController:ReportPiecePocketed] ⚠️ Not my shot — ignoring {pieceId}");
    }

    public void ReportStrikerPocketed()
    {
        Debug.LogWarning($"[StrikerController:ReportStrikerPocketed] _iAmCurrentShooter={_iAmCurrentShooter}");
        if (_iAmCurrentShooter) _strikerPocketed = true;
    }

    private void OnDestroy()
    {
        if (CarromNetworkManager.Instance != null)
        {
            CarromNetworkManager.Instance.OnGameStart -= OnGameStart;
            CarromNetworkManager.Instance.OnShotFired -= OnOpponentShot;
            CarromNetworkManager.Instance.OnTurnUpdate -= OnTurnUpdate;
            CarromNetworkManager.Instance.OnSyncPositions -= OnReceiveSyncPositions;
        }
        Debug.Log("[StrikerController:OnDestroy] Unsubscribed");
    }
}