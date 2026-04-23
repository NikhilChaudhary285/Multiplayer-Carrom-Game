using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Lobby Panel")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private TMP_InputField betAmountInput;
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private Button createRoomBtn;
    [SerializeField] private Button joinRoomBtn;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Waiting Panel")]
    [SerializeField] private GameObject waitingPanel;
    [SerializeField] private TextMeshProUGUI roomCodeDisplay;

    [Header("Game HUD")]
    [SerializeField] private GameObject gameHUD;
    [SerializeField] private TextMeshProUGUI myScoreText;
    [SerializeField] private TextMeshProUGUI opponentScoreText;
    [SerializeField] private TextMeshProUGUI turnIndicatorText;
    [SerializeField] private TextMeshProUGUI myNameText;
    [SerializeField] private TextMeshProUGUI opponentNameText;
    [SerializeField] private TextMeshProUGUI potText;

    [Header("Game Over Panel")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI gameOverTitle;
    [SerializeField] private TextMeshProUGUI gameOverMessage;
    [SerializeField] private Button playAgainBtn;

    private void Start()
    {
        Debug.Log("[UIManager:Start] Initializing UI");

        if (createRoomBtn == null) Debug.LogError("[UIManager:Start] ❌ createRoomBtn is NULL — assign in Inspector!");
        if (joinRoomBtn == null) Debug.LogError("[UIManager:Start] ❌ joinRoomBtn is NULL — assign in Inspector!");
        if (playAgainBtn == null) Debug.LogError("[UIManager:Start] ❌ playAgainBtn is NULL — assign in Inspector!");
        if (CarromNetworkManager.Instance == null) Debug.LogError("[UIManager:Start] ❌ CarromNetworkManager.Instance is NULL!");

        createRoomBtn.onClick.AddListener(OnCreateRoom);
        joinRoomBtn.onClick.AddListener(OnJoinRoom);
        playAgainBtn.onClick.AddListener(OnPlayAgain);
        Debug.Log("[UIManager:Start] Button listeners added");

        var nm = CarromNetworkManager.Instance;
        nm.OnRoomCreated += d =>
        {
            Debug.Log($"[UIManager:OnRoomCreated] roomId={d.roomId}");
            ShowWaiting(d.roomId);
        };
        nm.OnPlayerJoined += d =>
        {
            Debug.Log($"[UIManager:OnPlayerJoined] Players count={d.players?.Count}");
            SetStatus("Player joined! Starting...");
        };
        nm.OnGameStart += OnGameStart;
        nm.OnTurnUpdate += OnTurnUpdate;
        nm.OnGameOver += OnGameOver;
        nm.OnError += msg =>
        {
            Debug.LogWarning($"[UIManager:OnError] Server error: {msg}");
            SetStatus($"⚠️ {msg}");
        };
        Debug.Log("[UIManager:Start] Network event listeners registered");

        ShowLobby();
    }

    private void OnCreateRoom()
    {
        string name = playerNameInput.text.Trim();
        string betStr = betAmountInput.text.Trim();
        Debug.Log($"[UIManager:OnCreateRoom] name='{name}' betStr='{betStr}'");

        if (!int.TryParse(betStr, out int bet) || bet <= 0 || string.IsNullOrEmpty(name))
        {
            Debug.LogWarning($"[UIManager:OnCreateRoom] Validation failed — name='{name}' bet='{betStr}'");
            SetStatus("Enter valid name and bet amount!");
            return;
        }
        Debug.Log($"[UIManager:OnCreateRoom] Calling CreateRoom({name}, {bet})");
        CarromNetworkManager.Instance.CreateRoom(name, bet);
    }

    private void OnJoinRoom()
    {
        string name = playerNameInput.text.Trim();
        string roomId = roomCodeInput.text.Trim().ToUpper();
        string betStr = betAmountInput.text.Trim();
        Debug.Log($"[UIManager:OnJoinRoom] name='{name}' roomId='{roomId}' betStr='{betStr}'");

        if (!int.TryParse(betStr, out int bet) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(roomId))
        {
            Debug.LogWarning($"[UIManager:OnJoinRoom] Validation failed — name='{name}' roomId='{roomId}' bet='{betStr}'");
            SetStatus("Fill all fields to join!");
            return;
        }
        Debug.Log($"[UIManager:OnJoinRoom] Calling JoinRoom({roomId}, {name}, {bet})");
        CarromNetworkManager.Instance.JoinRoom(roomId, name, bet);
    }

    private void OnGameStart(GameStartData data)
    {
        Debug.Log($"[UIManager:OnGameStart] currentTurn={data.currentTurn} betAmount={data.betAmount} winScore={data.winScore}");
        Debug.Log($"[UIManager:OnGameStart] Players: {data.players?.Count}");

        gameHUD.SetActive(true);
        lobbyPanel.SetActive(false);
        waitingPanel.SetActive(false);

        string myId = CarromNetworkManager.Instance.MySocketId;
        var me = data.players?.Find(p => p.id == myId);
        var opp = data.players?.Find(p => p.id != myId);

        Debug.Log($"[UIManager:OnGameStart] Me={me?.name} (id={me?.id}) | Opp={opp?.name} (id={opp?.id})");

        if (me == null) Debug.LogError("[UIManager:OnGameStart] ❌ Could not find local player in players list!");
        if (opp == null) Debug.LogError("[UIManager:OnGameStart] ❌ Could not find opponent in players list!");

        myNameText.text = me?.name ?? "You";
        opponentNameText.text = opp?.name ?? "Opponent";
        potText.text = $"🪙 Pot: {data.betAmount * 2}";
        UpdateScores(0, 0);
        SetTurnText(data.currentTurn == myId);
    }

    private void OnTurnUpdate(TurnUpdateData data)
    {
        string myId = CarromNetworkManager.Instance.MySocketId;
        var me = data.scores?.Find(p => p.id == myId);
        var opp = data.scores?.Find(p => p.id != myId);

        Debug.Log($"[UIManager:OnTurnUpdate] currentTurn={data.currentTurn} IsMyTurn={data.currentTurn == myId} myScore={me?.score} oppScore={opp?.score}");

        UpdateScores(me?.score ?? 0, opp?.score ?? 0);
        SetTurnText(data.currentTurn == myId);
    }

    private void OnGameOver(GameOverData data)
    {
        bool iWon = data.winnerId == CarromNetworkManager.Instance.MySocketId;
        Debug.Log($"[UIManager:OnGameOver] winnerId={data.winnerId} myId={CarromNetworkManager.Instance.MySocketId} iWon={iWon} message={data.message}");

        gameOverPanel.SetActive(true);
        gameHUD.SetActive(false);
        gameOverTitle.text = iWon ? "🏆 YOU WIN!" : "😔 You Lose";
        gameOverMessage.text = data.message;
    }

    private void OnPlayAgain()
    {
        Debug.Log("[UIManager:OnPlayAgain] Reloading scene 0");
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
    }

    private void ShowLobby()
    {
        Debug.Log("[UIManager:ShowLobby] Showing lobby panel");
        lobbyPanel.SetActive(true);
        waitingPanel.SetActive(false);
        gameHUD.SetActive(false);
        gameOverPanel.SetActive(false);
    }

    private void ShowWaiting(string roomId)
    {
        Debug.Log($"[UIManager:ShowWaiting] roomId={roomId}");
        waitingPanel.SetActive(true);
        lobbyPanel.SetActive(false);
        roomCodeDisplay.text = $"Room Code:\n{roomId}";
    }

    private void SetStatus(string msg)
    {
        Debug.Log($"[UIManager:SetStatus] {msg}");
        if (statusText) statusText.text = msg;
        else Debug.LogWarning("[UIManager:SetStatus] statusText is NULL — assign in Inspector!");
    }

    private void UpdateScores(int my, int opp)
    {
        Debug.Log($"[UIManager:UpdateScores] my={my} opp={opp}");
        myScoreText.text = $"{my}/160";
        opponentScoreText.text = $"{opp}/160";
    }

    private void SetTurnText(bool isMyTurn)
    {
        Debug.Log($"[UIManager:SetTurnText] isMyTurn={isMyTurn}");
        turnIndicatorText.text = isMyTurn ? "🟢 Your Turn" : "⏳ Opponent's Turn";
        turnIndicatorText.color = isMyTurn ? Color.green : Color.gray;
    }
}