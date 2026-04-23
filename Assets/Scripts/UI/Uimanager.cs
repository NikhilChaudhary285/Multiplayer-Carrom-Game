using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages all UI panels: Lobby, GameHUD, GameOver.
/// Wire up all references in Inspector.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("Lobby Panel")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private TMP_InputField betAmountInput;
    [SerializeField] private TMP_InputField roomCodeInput; // for joining
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
        // Button listeners
        createRoomBtn.onClick.AddListener(OnCreateRoom);
        joinRoomBtn.onClick.AddListener(OnJoinRoom);
        playAgainBtn.onClick.AddListener(OnPlayAgain);

        // Network events
        var nm = CarromNetworkManager.Instance;
        nm.OnRoomCreated += d => { ShowWaiting(d.roomId); };
        nm.OnPlayerJoined += d => SetStatus("Player joined! Starting...");
        nm.OnGameStart += OnGameStart;
        nm.OnTurnUpdate += OnTurnUpdate;
        nm.OnGameOver += OnGameOver;
        nm.OnError += msg => SetStatus($"⚠️ {msg}");

        ShowLobby();
    }

    // ─── LOBBY ───

    private void OnCreateRoom()
    {
        string name = playerNameInput.text.Trim();
        if (!int.TryParse(betAmountInput.text, out int bet) || bet <= 0 || string.IsNullOrEmpty(name))
        {
            SetStatus("Enter valid name and bet amount!");
            return;
        }
        CarromNetworkManager.Instance.CreateRoom(name, bet);
    }

    private void OnJoinRoom()
    {
        string name = playerNameInput.text.Trim();
        string roomId = roomCodeInput.text.Trim().ToUpper();
        if (!int.TryParse(betAmountInput.text, out int bet) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(roomId))
        {
            SetStatus("Fill all fields to join!");
            return;
        }
        CarromNetworkManager.Instance.JoinRoom(roomId, name, bet);
    }

    // ─── GAME ───

    private void OnGameStart(GameStartData data)
    {
        gameHUD.SetActive(true);
        lobbyPanel.SetActive(false);
        waitingPanel.SetActive(false);

        string myId = CarromNetworkManager.Instance.MySocketId;
        var me = data.players.Find(p => p.id == myId);
        var opp = data.players.Find(p => p.id != myId);

        myNameText.text = me?.name ?? "You";
        opponentNameText.text = opp?.name ?? "Opponent";
        potText.text = $"🪙 Pot: {data.betAmount * 2}";
        UpdateScores(0, 0);
        SetTurnText(data.currentTurn == myId);
    }

    private void OnTurnUpdate(TurnUpdateData data)
    {
        string myId = CarromNetworkManager.Instance.MySocketId;
        var me = data.scores.Find(p => p.id == myId);
        var opp = data.scores.Find(p => p.id != myId);
        UpdateScores(me?.score ?? 0, opp?.score ?? 0);
        SetTurnText(data.currentTurn == myId);
    }

    private void OnGameOver(GameOverData data)
    {
        gameOverPanel.SetActive(true);
        gameHUD.SetActive(false);
        bool iWon = data.winnerId == CarromNetworkManager.Instance.MySocketId;
        gameOverTitle.text = iWon ? "🏆 YOU WIN!" : "😔 You Lose";
        gameOverMessage.text = data.message;
    }

    private void OnPlayAgain() => UnityEngine.SceneManagement.SceneManager.LoadScene(0);

    // ─── HELPERS ───

    private void ShowLobby() { lobbyPanel.SetActive(true); waitingPanel.SetActive(false); gameHUD.SetActive(false); gameOverPanel.SetActive(false); }
    private void ShowWaiting(string roomId) { waitingPanel.SetActive(true); lobbyPanel.SetActive(false); roomCodeDisplay.text = $"Room Code:\n{roomId}"; }
    private void SetStatus(string msg) { if (statusText) statusText.text = msg; }
    private void UpdateScores(int my, int opp) { myScoreText.text = $"{my}/160"; opponentScoreText.text = $"{opp}/160"; }
    private void SetTurnText(bool isMyTurn) { turnIndicatorText.text = isMyTurn ? "🟢 Your Turn" : "⏳ Opponent's Turn"; turnIndicatorText.color = isMyTurn ? Color.green : Color.gray; }
}