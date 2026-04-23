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
    [SerializeField] private Transform strikerSlideArea;

    private Rigidbody2D _rb;
    private Vector2 _dragStart;
    private bool _isDragging = false;
    private bool _shotFired = false;
    private bool _canShoot = false;
    private bool _isHost = false;

    private List<string> _pocketedThisTurn = new List<string>();
    private bool _strikerPocketed = false;

    [SerializeField] private CarromBoard boardRef;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        Debug.Log($"[StrikerController:Awake] Rigidbody2D found={_rb != null}");
        if (aimLine == null) Debug.LogWarning("[StrikerController:Awake] aimLine is not assigned in Inspector!");
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
        Debug.Log("[StrikerController:Start] Events subscribed ✅");
    }

    private void OnGameStart(GameStartData data)
    {
        string myId = CarromNetworkManager.Instance.MySocketId;
        _canShoot = data.currentTurn == myId;
        var me = data.players?.Find(p => p.id == myId);
        _isHost = me != null && me.isHost;
        Debug.Log($"[StrikerController:OnGameStart] myId={myId} currentTurn={data.currentTurn} _canShoot={_canShoot} _isHost={_isHost}");
        ResetStriker();
    }

    private void OnTurnUpdate(TurnUpdateData data)
    {
        _canShoot = data.currentTurn == CarromNetworkManager.Instance.MySocketId;
        _shotFired = false;
        _pocketedThisTurn.Clear();
        _strikerPocketed = false;
        Debug.Log($"[StrikerController:OnTurnUpdate] currentTurn={data.currentTurn} _canShoot={_canShoot} — resetting striker");
        ResetStriker();
    }

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
        float dist = Vector2.Distance(mousePos, transform.position);
        Debug.Log($"[StrikerController:OnDragStart] mousePos={mousePos} strikerPos={transform.position} dist={dist:F3}");
        if (dist < 0.5f)
        {
            _dragStart = mousePos;
            _isDragging = true;
            Debug.Log("[StrikerController:OnDragStart] Drag started ✅");
        }
        else
        {
            Debug.Log("[StrikerController:OnDragStart] Click too far from striker — ignored");
        }
    }

    private void OnDragUpdate()
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dragDir = _dragStart - mousePos;
        float dragDist = Mathf.Clamp(dragDir.magnitude, 0, maxDragDistance);

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

        Debug.Log($"[StrikerController:OnDragRelease] dragDist={dragDist:F3} maxDrag={maxDragDistance}");

        if (dragDist < 0.05f)
        {
            Debug.LogWarning("[StrikerController:OnDragRelease] Drag too small — shot ignored");
            return;
        }

        float power = dragDist / maxDragDistance;
        float angle = Mathf.Atan2(dragDir.y, dragDir.x) * Mathf.Rad2Deg;
        Debug.Log($"[StrikerController:OnDragRelease] power={power:F3} angle={angle:F2}deg — firing!");
        FireStriker(angle, power);
    }

    private void FireStriker(float angle, float power)
    {
        Debug.Log($"[StrikerController:FireStriker] angle={angle:F2} power={power:F3} maxPower={maxPower}");
        _shotFired = true;
        _canShoot = false;

        Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        Debug.Log($"[StrikerController:FireStriker] Applying force direction={direction} magnitude={power * maxPower}");
        _rb.AddForce(direction * power * maxPower);

        CarromNetworkManager.Instance.SendShot(angle, power);
        Debug.Log("[StrikerController:FireStriker] Shot sent to server — starting settle coroutine");
        StartCoroutine(WaitForShotSettle());
    }

    private void OnOpponentShot(ShotFiredData data)
    {
        Debug.Log($"[StrikerController:OnOpponentShot] Applying opponent shot — angle={data.angle} power={data.power}");
        Vector2 direction = new Vector2(
            Mathf.Cos(data.angle * Mathf.Deg2Rad),
            Mathf.Sin(data.angle * Mathf.Deg2Rad)
        );
        _rb.AddForce(direction * data.power * maxPower);
        Debug.Log("[StrikerController:OnOpponentShot] Force applied — starting settle coroutine");
        StartCoroutine(WaitForShotSettle());
    }

    private IEnumerator WaitForShotSettle()
    {
        Debug.Log("[StrikerController:WaitForShotSettle] Waiting 0.5s initial delay...");
        yield return new WaitForSeconds(0.5f);

        float settleTimer = 0f;
        float settleTimeout = 5f;
        int frameCount = 0;

        Debug.Log("[StrikerController:WaitForShotSettle] Starting settle poll loop (timeout=5s)");
        while (settleTimer < settleTimeout)
        {
            if (AllPiecesSettled())
            {
                Debug.Log($"[StrikerController:WaitForShotSettle] All pieces settled after {settleTimer:F2}s ({frameCount} frames)");
                break;
            }
            settleTimer += Time.deltaTime;
            frameCount++;
            yield return null;
        }

        if (settleTimer >= settleTimeout)
            Debug.LogWarning($"[StrikerController:WaitForShotSettle] ⚠️ Settle TIMEOUT reached! Pieces may still be moving.");

        Debug.Log($"[StrikerController:WaitForShotSettle] Reporting result — pocketed={string.Join(",", _pocketedThisTurn)} strikerPocketed={_strikerPocketed}");
        CarromNetworkManager.Instance.SendShotResult(_pocketedThisTurn, _strikerPocketed);
    }

    private bool AllPiecesSettled()
    {
        var pieces = FindObjectsByType<CarromPiece>(FindObjectsSortMode.None);
        foreach (var p in pieces)
        {
            var rb = p.GetComponent<Rigidbody2D>();
            if (rb != null && rb.linearVelocity.magnitude > 0.05f)
                return false;
        }
        bool strikerSettled = _rb.linearVelocity.magnitude < 0.05f;
        return strikerSettled;
    }

    public void ReportPiecePocketed(string pieceId)
    {
        Debug.Log($"[StrikerController:ReportPiecePocketed] pieceId={pieceId}");
        _pocketedThisTurn.Add(pieceId);
    }

    public void ReportStrikerPocketed()
    {
        Debug.LogWarning("[StrikerController:ReportStrikerPocketed] ⚠️ Striker was pocketed! Penalty will apply.");
        _strikerPocketed = true;
    }

    private void ResetStriker()
    {
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        Vector3 pos = _isHost ? new Vector3(0, -3f, 0) : new Vector3(0, 3f, 0);
        transform.position = pos;
        Debug.Log($"[StrikerController:ResetStriker] Reset to pos={pos} isHost={_isHost} velocity zeroed");
    }

    private void OnDestroy()
    {
        Debug.Log("[StrikerController:OnDestroy] Unsubscribing from network events");
        if (CarromNetworkManager.Instance != null)
        {
            CarromNetworkManager.Instance.OnGameStart -= OnGameStart;
            CarromNetworkManager.Instance.OnShotFired -= OnOpponentShot;
            CarromNetworkManager.Instance.OnTurnUpdate -= OnTurnUpdate;
        }
    }
}