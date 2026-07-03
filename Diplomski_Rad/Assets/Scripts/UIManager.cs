// UIManager.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices:
//   • All references are resolved by name in Start() — no Inspector drag-and-drop.
//     This makes the scene build script self-contained and keeps the component
//     usable even after scene re-build.
//   • Button listeners are added in code (AddListener) so they survive domain
//     reload without needing serialised UnityEvent data.
//   • Application.Quit is guarded by #if !UNITY_EDITOR so the quit flow behaves
//     correctly in both Editor and build.
//   • UpdateScore is public so any game system can call it without a direct
//     reference back to the UI — reduces coupling.
//   • VolumeSlider lives inside OptionsPanel (inactive at start); found via
//     GetComponentsInChildren(includeInactive:true) to bypass the inactive parent.
//   • ShowMainMenu / HideMainMenu manage Time.timeScale so gameplay is paused
//     while the menu is open and resumes the moment the player clicks Play.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • Any UI element not found by name: logs a clear error and continues.
//     The missing element's feature is disabled but the rest of the UI still works.
//   • ScoreText destroyed at runtime: UpdateScore logs an error instead of
//     throwing a NullReferenceException.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    // ── Cached references (resolved by name in Start) ─────────────────────────
    private GameObject menuPanel;
    private Button     startButton;
    private Button     optionsButton;
    private Button     quitButton;
    private Slider     volumeSlider;
    private Text       scoreText;
    private GameObject optionsPanel;
    private GameObject gameOverPanel;
    private Button     respawnButton;
    // Resolved lazily on first use rather than at Start — PlayerController and
    // UIManager don't have a guaranteed resolution order relative to each
    // other, and this is only ever needed once the player has actually died.
    private PlayerController player;

    // ── Element / dash HUD references ────────────────────────────────────────
    private Text   elementDisplayText;
    private Image  elementDisplayImage;
    private Text   dashChargesText;
    private Text   windIndicatorText;
    private Text   healthDisplayText;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        ResolveReferences();
        WireListeners();
        ShowMainMenu();   // start paused with menu open
    }

    private void Update()
    {
        // Escape key re-opens the menu when the game is running — but not
        // over the Game Over screen, or Respawn would end up hidden behind it.
        bool gameOverShowing = gameOverPanel != null && gameOverPanel.activeSelf;
        if (menuPanel != null && !menuPanel.activeSelf && !gameOverShowing && Input.GetKeyDown(KeyCode.Escape))
            ShowMainMenu();
    }

    // ── Menu control ──────────────────────────────────────────────────────────

    public void ShowMainMenu()
    {
        if (menuPanel != null) menuPanel.SetActive(true);
        if (optionsPanel != null) optionsPanel.SetActive(false);
        Time.timeScale = 0f;
    }

    public void HideMainMenu()
    {
        if (menuPanel != null) menuPanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    /// <summary>Called by PlayerController when health reaches 0. Pauses gameplay.</summary>
    public void ShowGameOver()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        Time.timeScale = 0f;
    }

    /// <summary>
    /// Called by PlayerController.Respawn() so the screen is always cleared
    /// consistently, regardless of whether Respawn was triggered by the
    /// button below or the R-key debug shortcut.
    /// </summary>
    public void HideGameOver()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    // ── Reference resolution ──────────────────────────────────────────────────

    private void ResolveReferences()
    {
        // Search the entire Canvas hierarchy including inactive GameObjects.
        // GameObject.Find skips inactive objects so OptionsPanel (which starts hidden)
        // would never be found that way.
        var rects = GetComponentsInChildren<RectTransform>(true);
        foreach (var rt in rects)
        {
            switch (rt.gameObject.name)
            {
                case "MenuPanel":
                    menuPanel = rt.gameObject;
                    break;
                case "OptionsPanel":
                    optionsPanel = rt.gameObject;
                    break;
                case "GameOverPanel":
                    gameOverPanel = rt.gameObject;
                    break;
                case "RespawnButton":
                    respawnButton = rt.GetComponent<Button>();
                    break;
                case "StartButton":
                    startButton = rt.GetComponent<Button>();
                    break;
                case "OptionsButton":
                    optionsButton = rt.GetComponent<Button>();
                    break;
                case "QuitButton":
                    quitButton = rt.GetComponent<Button>();
                    break;
                case "VolumeSlider":
                    volumeSlider = rt.GetComponent<Slider>();
                    break;
                case "ScoreText":
                    scoreText = rt.GetComponent<Text>();
                    break;
                case "ElementDisplay":
                    elementDisplayImage = rt.GetComponent<Image>();
                    break;
                case "ElementDisplayText":
                    elementDisplayText = rt.GetComponent<Text>();
                    break;
                case "DashChargesDisplay":
                    dashChargesText = rt.GetComponent<Text>();
                    break;
                case "HealthDisplay":
                    healthDisplayText = rt.GetComponent<Text>();
                    break;
                case "WindIndicator":
                    windIndicatorText = rt.GetComponent<Text>();
                    if (windIndicatorText != null)
                        windIndicatorText.gameObject.SetActive(false);
                    break;
            }
        }

        if (menuPanel     == null) Debug.LogError("[UIManager] 'MenuPanel' not found in Canvas hierarchy. Rebuild the scene.");
        if (optionsPanel  == null) Debug.LogError("[UIManager] 'OptionsPanel' not found in Canvas hierarchy.");
        if (gameOverPanel == null) Debug.LogError("[UIManager] 'GameOverPanel' not found in Canvas hierarchy. Rebuild the scene.");
        if (respawnButton == null) Debug.LogError("[UIManager] 'RespawnButton' not found in Canvas hierarchy.");
        if (scoreText     == null) Debug.LogWarning("[UIManager] 'ScoreText' not found.");
        if (volumeSlider  == null) Debug.LogWarning("[UIManager] 'VolumeSlider' not found.");

        if (gameOverPanel != null) gameOverPanel.SetActive(false); // safety: never start showing
    }

    // ── Button wiring ─────────────────────────────────────────────────────────

    private void WireListeners()
    {
        if (startButton   != null) startButton.onClick.AddListener(OnStartClicked);
        if (optionsButton != null) optionsButton.onClick.AddListener(OnOptionsClicked);
        if (quitButton    != null) quitButton.onClick.AddListener(OnQuitClicked);
        if (volumeSlider  != null) volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        if (respawnButton != null) respawnButton.onClick.AddListener(OnRespawnClicked);
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnStartClicked()
    {
        HideMainMenu();   // dismiss menu and resume gameplay; no scene load
    }

    private void OnOptionsClicked()
    {
        if (optionsPanel == null)
        {
            Debug.LogError("[UIManager] OnOptionsClicked: optionsPanel is null.");
            return;
        }
        optionsPanel.SetActive(!optionsPanel.activeSelf);
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnVolumeChanged(float value)
    {
        AudioListener.volume = value;
    }

    private void OnRespawnClicked()
    {
        if (player == null)
            player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();

        if (player != null)
        {
            // PlayerController.Respawn() itself calls HideGameOver() and
            // resumes Time.timeScale — kept in one place so the result is the
            // same regardless of what triggered the respawn.
            player.Respawn();
        }
        else
        {
            Debug.LogError("[UIManager] OnRespawnClicked: no PlayerController found in scene – " +
                           "hiding the Game Over screen directly instead.");
            HideGameOver();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void UpdateScore(int newScore)
    {
        if (scoreText == null)
        {
            Debug.LogError("[UIManager] UpdateScore: scoreText reference is null.");
            return;
        }
        scoreText.text = "Score: " + newScore;
    }

    public void UpdateElementDisplay(ElementStats stats)
    {
        if (elementDisplayText != null)
            elementDisplayText.text = stats.elementName;

        if (elementDisplayImage != null)
            elementDisplayImage.color = new Color(
                stats.elementColor.r,
                stats.elementColor.g,
                stats.elementColor.b,
                0.85f);
    }

    public void UpdateDashCharges(int current, int max)
    {
        if (dashChargesText == null) return;

        if (max <= 0)
        {
            Debug.LogError($"[UIManager] UpdateDashCharges: max ({max}) must be > 0.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < max; i++)
            sb.Append(i < current ? "●" : "○");
        dashChargesText.text = sb.ToString();
    }

    /// <summary>Updates the top-left player HP dot display. Called by PlayerController
    /// on init, on damage, and on respawn.</summary>
    public void UpdateHealthDisplay(int current, int max)
    {
        if (healthDisplayText == null) return;

        if (max <= 0)
        {
            Debug.LogError($"[UIManager] UpdateHealthDisplay: max ({max}) must be > 0.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < max; i++)
            sb.Append(i < current ? "●" : "○");
        healthDisplayText.text = sb.ToString();
    }

    /// <summary>
    /// Shows or hides the wind direction indicator at the top of the screen.
    /// Called by WindZoneController at the start and end of each wind phase.
    /// </summary>
    public void ShowWindIndicator(bool show, Vector2 direction = default)
    {
        if (windIndicatorText == null) return;
        windIndicatorText.gameObject.SetActive(show);
        if (show)
        {
            string arrow = direction.x >= 0f ? ">> WIND >>" : "<< WIND <<";
            windIndicatorText.text = arrow;
        }
    }
}
