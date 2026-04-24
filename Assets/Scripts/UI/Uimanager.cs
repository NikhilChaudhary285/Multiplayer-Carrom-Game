using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

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

    private void Awake()
    {
        Debug.Log("[NetworkManager:Awake] Initializing singleton");
        if (Instance != null)
        {
            Debug.LogWarning("[NetworkManager:Awake] Duplicate — destroying this one");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[NetworkManager:Awake] ✅ Singleton ready");
    }

    private void Start()
    {
        Debug.Log("[UIManager:Start] Initializing");

        // Inspector null checks
        if (lobbyPanel == null) Debug.LogError("[UIManager:Start] ❌ lobbyPanel NULL");
        if (waitingPanel == null) Debug.LogError("[UIManager:Start] ❌ waitingPanel NULL");
        if (gameHUD == null) Debug.LogError("[UIManager:Start] ❌ gameHUD NULL");
        if (gameOverPanel == null) Debug.LogError("[UIManager:Start] ❌ gameOverPanel NULL");
        if (createRoomBtn == null) Debug.LogError("[UIManager:Start] ❌ createRoomBtn NULL");
        if (joinRoomBtn == null) Debug.LogError("[UIManager:Start] ❌ joinRoomBtn NULL");
        if (playAgainBtn == null) Debug.LogError("[UIManager:Start] ❌ playAgainBtn NULL");
        if (statusText == null) Debug.LogWarning("[UIManager:Start] ⚠️ statusText NULL");
        if (CarromNetworkManager.Instance == null)
            Debug.LogError("[UIManager:Start] ❌ CarromNetworkManager.Instance NULL — ensure it exists in scene!");

        createRoomBtn.onClick.AddListener(OnCreateRoom);
        joinRoomBtn.onClick.AddListener(OnJoinRoom);
        playAgainBtn.onClick.AddListener(OnPlayAgain);

        var nm = CarromNetworkManager.Instance;
        nm.OnRoomCreated += d => { Debug.Log($"[UIManager] OnRoomCreated roomId={d.roomId}"); ShowWaiting(d.roomId); };
        nm.OnRoomJoined += d => { Debug.Log($"[UIManager] OnRoomJoined roomId={d.roomId}"); };
        nm.OnPlayerJoined += d => { Debug.Log($"[UIManager] OnPlayerJoined count={d.players?.Count}"); SetStatus("Player joined! Starting..."); };
        nm.OnGameStart += OnGameStart;
        nm.OnTurnUpdate += OnTurnUpdate;
        nm.OnGameOver += OnGameOver;
        nm.OnError += msg => { Debug.LogWarning($"[UIManager] OnError: {msg}"); SetStatus($"⚠️ {msg}"); };

        Debug.Log("[UIManager:Start] ✅ Ready");
        ShowLobby();
    }

    private void OnCreateRoom()
    {
        string name = playerNameInput.text.Trim();
        string betStr = betAmountInput.text.Trim();
        Debug.Log($"[UIManager:OnCreateRoom] name='{name}' bet='{betStr}'");

        if (string.IsNullOrEmpty(name) || !int.TryParse(betStr, out int bet) || bet <= 0)
        {
            Debug.LogWarning("[UIManager:OnCreateRoom] Validation failed");
            SetStatus("Enter valid name and bet amount!");
            return;
        }
        CarromNetworkManager.Instance.CreateRoom(name, bet);
    }

    private void OnJoinRoom()
    {
        string name = playerNameInput.text.Trim();
        string roomId = roomCodeInput.text.Trim().ToUpper();
        string betStr = betAmountInput.text.Trim();
        Debug.Log($"[UIManager:OnJoinRoom] name='{name}' roomId='{roomId}' bet='{betStr}'");

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(roomId) || !int.TryParse(betStr, out int bet) || bet <= 0)
        {
            Debug.LogWarning("[UIManager:OnJoinRoom] Validation failed");
            SetStatus("Fill all fields to join!");
            return;
        }
        CarromNetworkManager.Instance.JoinRoom(roomId, name, bet);
    }

    private void OnGameStart(GameStartData data)
    {
        string myId = CarromNetworkManager.Instance.MySocketId;
        var me = data.players?.Find(p => p.id == myId);
        var opp = data.players?.Find(p => p.id != myId);

        Debug.Log($"[UIManager:OnGameStart] myId={myId} currentTurn={data.currentTurn} IsMyTurn={data.currentTurn == myId}");
        Debug.Log($"[UIManager:OnGameStart] Me={me?.name} | Opp={opp?.name}");

        if (me == null) Debug.LogError("[UIManager:OnGameStart] ❌ Local player not found in list! MySocketId may be wrong.");
        if (opp == null) Debug.LogError("[UIManager:OnGameStart] ❌ Opponent not found in list!");

        lobbyPanel.SetActive(false);
        waitingPanel.SetActive(false);
        gameHUD.SetActive(true);
        gameOverPanel.SetActive(false);

        myNameText.text = me?.name ?? "You";
        opponentNameText.text = opp?.name ?? "Opponent";
        potText.text = $"Pot: {data.betAmount * 2}";
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
        Debug.Log($"[UIManager:OnGameOver] iWon={iWon} winner={data.winnerName} pot={data.totalPot}");
        gameHUD.SetActive(false);
        gameOverPanel.SetActive(true);
        gameOverTitle.text = iWon ? ":) YOU WIN!" : ":( You Lose";
        gameOverMessage.text = data.message;
    }

    private void OnPlayAgain()
    {
        Debug.Log("[UIManager:OnPlayAgain] Reloading scene 0");
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        gameOverPanel.SetActive(false);
        lobbyPanel.SetActive(true);
        ResetUI();
    }

    private void ShowLobby()
    {
        lobbyPanel.SetActive(true);
        waitingPanel.SetActive(false);
        gameHUD.SetActive(false);
        gameOverPanel.SetActive(false);
        Debug.Log("[UIManager:ShowLobby] Lobby shown");
    }

    private void ShowWaiting(string roomId)
    {
        lobbyPanel.SetActive(false);
        waitingPanel.SetActive(true);
        gameHUD.SetActive(false);
        gameOverPanel.SetActive(false);
        roomCodeDisplay.text = $"Room Code:\n{roomId}";
        Debug.Log($"[UIManager:ShowWaiting] roomId={roomId}");
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        else Debug.LogWarning($"[UIManager:SetStatus] statusText NULL — msg was: {msg}");
    }

    private void UpdateScores(int my, int opp)
    {
        if (myScoreText) myScoreText.text = $"{my}/160";
        if (opponentScoreText) opponentScoreText.text = $"{opp}/160";
        Debug.Log($"[UIManager:UpdateScores] my={my} opp={opp}");
    }

    private void SetTurnText(bool isMyTurn)
    {
        if (turnIndicatorText == null) { Debug.LogError("[UIManager:SetTurnText] turnIndicatorText NULL!"); return; }
        turnIndicatorText.text = isMyTurn ? "Your Turn" : "Opponent's Turn";
        turnIndicatorText.color = isMyTurn ? Color.green : Color.gray;
        Debug.Log($"[UIManager:SetTurnText] isMyTurn={isMyTurn}");
    }

    private void ResetUI()
    {
        #region Lobby Panel Text 
         playerNameInput.text = "";
         betAmountInput.text = "";
         roomCodeInput.text = "";
         statusText.text = "";
        #endregion Lobby Panel Text

        #region Waiting Panel Text 
         roomCodeDisplay.text = "";
        #endregion Waiting Panel Text 

        #region Game HUD Panel Text 
         myScoreText.text = "";
         opponentScoreText.text = "";
         turnIndicatorText.text = "";
         myNameText.text = "";
         opponentNameText.text = "";
         potText.text = "";
        #endregion Game HUD Panel Text 

        #region Game Panel Text 
         gameOverTitle.text = "";
         gameOverMessage.text = "";
        #endregion Game Panel Text 
    }
}