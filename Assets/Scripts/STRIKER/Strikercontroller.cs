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
    [SerializeField] private float hostBaselineY = -3f;   // Host (Player 1) fires from bottom
    [SerializeField] private float guestBaselineY = 3f;   // Guest (Player 2) fires from top
    [SerializeField] private float strikerBaselineX = 0f;   // Center X

    private Rigidbody2D _rb;
    private Vector2 _dragStart;
    private bool _isDragging = false;
    private bool _dragInitiatedOnStriker = false;
    private bool _shotFired = false;
    private bool _canShoot = false;
    private bool _isHost = false;
    private bool _iAmCurrentShooter = false;

    // ✅ FIX: Block ALL input for a short window after reset
    // prevents the "click resets then immediately fires" bug
    private bool _inputBlocked = false;
    private float _inputBlockTimer = 0f;
    private const float INPUT_BLOCK_DURATION = 0.3f; // seconds to block input after reset

    // ✅ FIX: Track running coroutine so we can stop it on reset
    private Coroutine _settleCoroutine = null;

    private List<string> _pocketedThisTurn = new List<string>();
    private bool _strikerPocketed = false;

    // ✅ FIX: Store my socket id locally once game starts
    // so we never get a blank string when comparing
    private string _mySocketId = "";

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        if (_rb == null) Debug.LogError("[StrikerController:Awake] ❌ Rigidbody2D missing!");
        if (aimLine == null) Debug.LogWarning("[StrikerController:Awake] ⚠️ aimLine not assigned in Inspector");
        Debug.Log($"[StrikerController:Awake] dragClickRadius={dragClickRadius} maxPower={maxPower} hostY={hostBaselineY} guestY={guestBaselineY}");
    }

    private void Start()
    {
        Debug.Log("[StrikerController:Start] Subscribing to network events");
        if (CarromNetworkManager.Instance == null)
        {
            Debug.LogError("[StrikerController:Start] ❌ CarromNetworkManager.Instance is NULL!");
            return;
        }
        CarromNetworkManager.Instance.OnGameStart += OnGameStart;
        CarromNetworkManager.Instance.OnShotFired += OnOpponentShot;
        CarromNetworkManager.Instance.OnTurnUpdate += OnTurnUpdate;

        _canShoot = false;
        _inputBlocked = false;
        Debug.Log("[StrikerController:Start] ✅ Subscribed. Waiting for game_start.");
    }

    private void Update()
    {
        // ── INPUT BLOCK TIMER ──
        // After a reset we block input briefly so the click that
        // triggered the reset doesn't ALSO start a drag
        if (_inputBlocked)
        {
            _inputBlockTimer -= Time.deltaTime;
            if (_inputBlockTimer <= 0f)
            {
                _inputBlocked = false;
                Debug.Log("[StrikerController:Update] ✅ Input block lifted — player can now shoot");
            }
            return; // eat ALL input while blocked
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
                // Clean up stale drag state on any mouse-up
                _isDragging = false;
                _dragInitiatedOnStriker = false;
                if (aimLine != null) aimLine.enabled = false;
            }
        }
    }

    // ─────────────────────────────────────────
    // NETWORK CALLBACKS
    // ─────────────────────────────────────────

    private void OnGameStart(GameStartData data)
    {
        // ✅ FIX: Cache socket id NOW — guaranteed valid at game_start
        _mySocketId = CarromNetworkManager.Instance.MySocketId;
        if (string.IsNullOrEmpty(_mySocketId))
        {
            Debug.LogError("[StrikerController:OnGameStart] ❌ MySocketId is EMPTY at game_start! Turn logic will be broken.");
        }

        _canShoot = data.currentTurn == _mySocketId;

        // ✅ FIX: Find isHost from players list using cached id
        _isHost = false;
        if (data.players != null)
        {
            foreach (var p in data.players)
            {
                Debug.Log($"[StrikerController:OnGameStart] Player in list: id={p.id} name={p.name} isHost={p.isHost}");
                if (p.id == _mySocketId)
                {
                    _isHost = p.isHost;
                    Debug.Log($"[StrikerController:OnGameStart] ✅ Found ME: name={p.name} isHost={_isHost}");
                    break;
                }
            }
        }

        Debug.Log($"[StrikerController:OnGameStart] myId={_mySocketId} currentTurn={data.currentTurn} _canShoot={_canShoot} _isHost={_isHost}");

        StopSettleCoroutine();
        ResetTurnState();
        ResetStrikerPhysicsAndPosition();
        BlockInputBriefly();
    }

    private void OnTurnUpdate(TurnUpdateData data)
    {
        // ✅ FIX: Re-cache id in case it was blank before
        if (string.IsNullOrEmpty(_mySocketId))
        {
            _mySocketId = CarromNetworkManager.Instance.MySocketId;
            Debug.LogWarning($"[StrikerController:OnTurnUpdate] _mySocketId was empty — re-cached: {_mySocketId}");
        }

        bool wasMyTurn = _canShoot;
        _canShoot = data.currentTurn == _mySocketId;

        Debug.Log($"[StrikerController:OnTurnUpdate] currentTurn={data.currentTurn} myId={_mySocketId} wasMyTurn={wasMyTurn} → _canShoot={_canShoot}");

        StopSettleCoroutine();
        ResetTurnState();
        ResetStrikerPhysicsAndPosition();
        BlockInputBriefly(); // ✅ Always block briefly on turn change to prevent phantom clicks
    }

    // ─────────────────────────────────────────
    // RESET HELPERS
    // ─────────────────────────────────────────

    /// <summary>Clears all logical turn state (flags, lists). Does NOT touch physics.</summary>
    private void ResetTurnState()
    {
        _isDragging = false;
        _dragInitiatedOnStriker = false;
        _shotFired = false;
        _iAmCurrentShooter = false;
        _pocketedThisTurn.Clear();
        _strikerPocketed = false;
        if (aimLine != null) aimLine.enabled = false;
        Debug.Log("[StrikerController:ResetTurnState] ✅ All turn state cleared");
    }

    /// <summary>Zeros velocity and moves striker to correct baseline for this player.</summary>
    private void ResetStrikerPhysicsAndPosition()
    {
        // ✅ FIX: Use RigidbodyConstraints to fully freeze during reset,
        // then unfreeze. Prevents residual forces from carrying over.
        _rb.constraints = RigidbodyConstraints2D.FreezeAll;

        // Zero out EVERYTHING
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;

        // ✅ FIX: Choose Y based on _isHost which is now set correctly
        float targetY = _isHost ? hostBaselineY : guestBaselineY;
        Vector3 targetPos = new Vector3(strikerBaselineX, targetY, 0f);
        transform.position = targetPos;

        Debug.Log($"[StrikerController:ResetStrikerPhysicsAndPosition] ✅ pos={targetPos} isHost={_isHost} _canShoot={_canShoot}");

        // Unfreeze after one frame so physics engine picks up new position cleanly
        StartCoroutine(UnfreezeNextFrame());
    }

    private IEnumerator UnfreezeNextFrame()
    {
        yield return null; // wait exactly one physics frame
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation; // allow movement but no rotation
        Debug.Log("[StrikerController:UnfreezeNextFrame] ✅ Rigidbody unfrozen");
    }

    /// <summary>Blocks player input for INPUT_BLOCK_DURATION seconds.</summary>
    private void BlockInputBriefly()
    {
        _inputBlocked = true;
        _inputBlockTimer = INPUT_BLOCK_DURATION;
        Debug.Log($"[StrikerController:BlockInputBriefly] Input blocked for {INPUT_BLOCK_DURATION}s");
    }

    private void StopSettleCoroutine()
    {
        if (_settleCoroutine != null)
        {
            StopCoroutine(_settleCoroutine);
            _settleCoroutine = null;
            Debug.Log("[StrikerController:StopSettleCoroutine] ✅ Previous settle coroutine stopped");
        }
    }

    // ─────────────────────────────────────────
    // DRAG INPUT
    // ─────────────────────────────────────────

    private void OnDragStart()
    {
        if (Camera.main == null) { Debug.LogError("[StrikerController:OnDragStart] ❌ Camera.main is NULL!"); return; }
        Vector2 mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        float dist = Vector2.Distance(mouse, transform.position);

        Debug.Log($"[StrikerController:OnDragStart] mouseWorld={mouse} strikerPos={transform.position} dist={dist:F3} threshold={dragClickRadius}");

        if (dist <= dragClickRadius)
        {
            _dragStart = mouse;
            _isDragging = true;
            _dragInitiatedOnStriker = true;
            Debug.Log("[StrikerController:OnDragStart] ✅ Drag STARTED on striker");
        }
        else
        {
            _isDragging = false;
            _dragInitiatedOnStriker = false;
            Debug.Log($"[StrikerController:OnDragStart] ❌ dist={dist:F3} > threshold — click ignored");
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
        _isDragging = false;
        _dragInitiatedOnStriker = false;
        if (aimLine != null) aimLine.enabled = false;

        if (Camera.main == null) { Debug.LogError("[StrikerController:OnDragRelease] ❌ Camera.main NULL"); return; }
        Vector2 mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dragDir = _dragStart - mouse;
        float dist = Mathf.Clamp(dragDir.magnitude, 0, maxDragDistance);

        Debug.Log($"[StrikerController:OnDragRelease] dragDist={dist:F3} min=0.05");

        if (dist < 0.05f)
        {
            Debug.LogWarning("[StrikerController:OnDragRelease] ❌ Drag too small — ignored");
            return;
        }

        float power = dist / maxDragDistance;
        float angle = Mathf.Atan2(dragDir.y, dragDir.x) * Mathf.Rad2Deg;
        Debug.Log($"[StrikerController:OnDragRelease] power={power:F3} angle={angle:F2}° — FIRING");
        FireStriker(angle, power);
    }

    // ─────────────────────────────────────────
    // FIRE — MY SHOT
    // ─────────────────────────────────────────

    private void FireStriker(float angle, float power)
    {
        if (!_canShoot)
        {
            Debug.LogError("[StrikerController:FireStriker] ❌ _canShoot=false — shot blocked");
            return;
        }
        if (_shotFired)
        {
            Debug.LogWarning("[StrikerController:FireStriker] ❌ Already fired this turn — ignored");
            return;
        }
        if (_inputBlocked)
        {
            Debug.LogWarning("[StrikerController:FireStriker] ❌ Input still blocked — ignored");
            return;
        }

        _shotFired = true;
        _canShoot = false;
        _iAmCurrentShooter = true;

        // ✅ Ensure constraints are clear before applying force
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        _rb.AddForce(dir * power * maxPower);

        Debug.Log($"[StrikerController:FireStriker] ✅ Fired! angle={angle:F2} power={power:F3} force={power * maxPower:F1} _iAmCurrentShooter=true");
        CarromNetworkManager.Instance.SendShot(angle, power);

        StopSettleCoroutine();
        _settleCoroutine = StartCoroutine(WaitForShotSettle());
    }

    // ─────────────────────────────────────────
    // OPPONENT SHOT — physics only, no reporting
    // ─────────────────────────────────────────

    private void OnOpponentShot(ShotFiredData data)
    {
        if (data.playerId == _mySocketId)
        {
            Debug.LogWarning("[StrikerController:OnOpponentShot] Got my own id — skipping");
            return;
        }

        Debug.Log($"[StrikerController:OnOpponentShot] Applying opponent shot angle={data.angle} power={data.power}");
        Debug.Log($"[StrikerController:OnOpponentShot] _iAmCurrentShooter={_iAmCurrentShooter} (must be FALSE)");

        // ✅ Ensure constraints are clear before applying force
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        Vector2 dir = new Vector2(Mathf.Cos(data.angle * Mathf.Deg2Rad), Mathf.Sin(data.angle * Mathf.Deg2Rad));
        _rb.AddForce(dir * data.power * maxPower);

        // ✅ NO settle coroutine — opponent reports their own result
        Debug.Log("[StrikerController:OnOpponentShot] ✅ Force applied. Not starting settle coroutine.");
    }

    // ─────────────────────────────────────────
    // SETTLE COROUTINE — shooter only
    // ─────────────────────────────────────────

    private IEnumerator WaitForShotSettle()
    {
        Debug.Log($"[StrikerController:WaitForShotSettle] Started. _iAmCurrentShooter={_iAmCurrentShooter}");

        if (!_iAmCurrentShooter)
        {
            Debug.LogError("[StrikerController:WaitForShotSettle] ❌ ABORTED — not the shooter!");
            yield break;
        }

        yield return new WaitForSeconds(0.5f);
        Debug.Log("[StrikerController:WaitForShotSettle] Initial delay done. Polling...");

        float timer = 0f, timeout = 6f;
        int frames = 0;

        while (timer < timeout)
        {
            if (AllPiecesSettled())
            {
                Debug.Log($"[StrikerController:WaitForShotSettle] ✅ Settled in {timer:F2}s ({frames} frames)");
                break;
            }
            timer += Time.deltaTime;
            frames++;
            yield return null;
        }

        if (timer >= timeout)
            Debug.LogWarning($"[StrikerController:WaitForShotSettle] ⚠️ Timeout {timeout}s — reporting anyway");

        Debug.Log($"[StrikerController:WaitForShotSettle] Result: pocketed=[{string.Join(",", _pocketedThisTurn)}] strikerPocketed={_strikerPocketed}");
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
        Debug.Log($"[StrikerController:ReportPiecePocketed] pieceId={pieceId} _iAmCurrentShooter={_iAmCurrentShooter}");
        if (_iAmCurrentShooter)
            _pocketedThisTurn.Add(pieceId);
        else
            Debug.LogWarning($"[StrikerController:ReportPiecePocketed] ⚠️ Ignoring {pieceId} — not my shot");
    }

    public void ReportStrikerPocketed()
    {
        Debug.LogWarning($"[StrikerController:ReportStrikerPocketed] _iAmCurrentShooter={_iAmCurrentShooter}");
        if (_iAmCurrentShooter) _strikerPocketed = true;
    }

    // ─────────────────────────────────────────
    // CLEANUP
    // ─────────────────────────────────────────

    private void OnDestroy()
    {
        Debug.Log("[StrikerController:OnDestroy] Unsubscribing");
        if (CarromNetworkManager.Instance != null)
        {
            CarromNetworkManager.Instance.OnGameStart -= OnGameStart;
            CarromNetworkManager.Instance.OnShotFired -= OnOpponentShot;
            CarromNetworkManager.Instance.OnTurnUpdate -= OnTurnUpdate;
        }
    }
}