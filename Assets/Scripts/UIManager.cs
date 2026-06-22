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
//   • The OptionsPanel is toggled (SetActive) rather than using alpha or scale
//     tricks — simplest approach; replace with an Animator if needed.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • Any UI element not found by name: logs a clear error and continues.
//     The missing element's feature is disabled but the rest of the UI still works.
//   • "Level1" scene not in Build Settings: SceneManager.LoadScene will throw;
//     caught and logged with guidance.
//   • ScoreText destroyed at runtime: UpdateScore logs an error instead of
//     throwing a NullReferenceException.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    // ── Cached references (resolved by name in Start) ─────────────────────────
    private Button startButton;
    private Button optionsButton;
    private Button quitButton;
    private Slider volumeSlider;
    private Text   scoreText;
    private GameObject optionsPanel;

    // ── Element / dash HUD references ───────────────────────────────────────────
    private Text   elementDisplayText;   // "Air", "Fire", etc.
    private Image  elementDisplayImage;  // coloured panel tint
    private Text   dashChargesText;      // e.g. "●●○"
    private Slider dashChargeMeter;      // fills while holding mouse button

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        ResolveReferences();
        WireListeners();
    }

    // ── Reference resolution ──────────────────────────────────────────────────

    private void ResolveReferences()
    {
        startButton   = FindButton("StartButton");
        optionsButton = FindButton("OptionsButton");
        quitButton    = FindButton("QuitButton");

        // Slider
        var sliderGO = GameObject.Find("VolumeSlider");
        if (sliderGO == null)
            Debug.LogError("[UIManager] 'VolumeSlider' not found in scene. " +
                           "Volume control will be unavailable. " +
                           "Run 'Tools/Build Platformer Scene' to rebuild the UI.");
        else
        {
            volumeSlider = sliderGO.GetComponent<Slider>();
            if (volumeSlider == null)
                Debug.LogError("[UIManager] 'VolumeSlider' exists but has no Slider component.");
        }

        // Score text
        var scoreGO = GameObject.Find("ScoreText");
        if (scoreGO == null)
            Debug.LogError("[UIManager] 'ScoreText' not found in scene. " +
                           "Score display will be unavailable.");
        else
        {
            scoreText = scoreGO.GetComponent<Text>();
            if (scoreText == null)
                Debug.LogError("[UIManager] 'ScoreText' exists but has no Text component.");
        }

        // Options panel
        optionsPanel = GameObject.Find("OptionsPanel");
        if (optionsPanel == null)
            Debug.LogError("[UIManager] 'OptionsPanel' not found in scene. " +
                           "Options toggle will be a no-op.");

        // ── Element / dash HUD ────────────────────────────────────────────────────
        var elemGO = GameObject.Find("ElementDisplay");
        if (elemGO == null)
            Debug.LogError("[UIManager] 'ElementDisplay' not found. " +
                           "Element name/colour display will be unavailable.");
        else
        {
            elementDisplayText  = elemGO.GetComponent<Text>();
            elementDisplayImage = elemGO.GetComponent<Image>();
            if (elementDisplayText == null && elementDisplayImage == null)
                Debug.LogError("[UIManager] 'ElementDisplay' has neither Text nor Image component.");
        }

        var chargesGO = GameObject.Find("DashChargesDisplay");
        if (chargesGO == null)
            Debug.LogError("[UIManager] 'DashChargesDisplay' not found. " +
                           "Dash charge counter will be unavailable.");
        else
        {
            dashChargesText = chargesGO.GetComponent<Text>();
            if (dashChargesText == null)
                Debug.LogError("[UIManager] 'DashChargesDisplay' has no Text component.");
        }

        var meterGO = GameObject.Find("DashChargeMeter");
        if (meterGO == null)
            Debug.LogError("[UIManager] 'DashChargeMeter' not found. " +
                           "Dash charge meter will be unavailable.");
        else
        {
            dashChargeMeter = meterGO.GetComponent<Slider>();
            if (dashChargeMeter == null)
                Debug.LogError("[UIManager] 'DashChargeMeter' has no Slider component.");
            else
            {
                dashChargeMeter.minValue = 0f;
                dashChargeMeter.maxValue = 1f;
                dashChargeMeter.value    = 0f;
            }
        }
    }

    private Button FindButton(string btnName)
    {
        var go = GameObject.Find(btnName);
        if (go == null)
        {
            Debug.LogError($"[UIManager] '{btnName}' not found in scene. " +
                           $"Its click handler will not be registered. " +
                           "Run 'Tools/Build Platformer Scene' to rebuild the UI.");
            return null;
        }

        var btn = go.GetComponent<Button>();
        if (btn == null)
            Debug.LogError($"[UIManager] '{btnName}' found but has no Button component.");

        return btn;
    }

    // ── Button wiring ─────────────────────────────────────────────────────────

    private void WireListeners()
    {
        if (startButton   != null) startButton.onClick.AddListener(OnStartClicked);
        if (optionsButton != null) optionsButton.onClick.AddListener(OnOptionsClicked);
        if (quitButton    != null) quitButton.onClick.AddListener(OnQuitClicked);

        if (volumeSlider  != null) volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnStartClicked()
    {
        try
        {
            // "Level1" must be present in File > Build Settings.
            // If missing, SceneManager logs a Unity error; we catch and augment it.
            SceneManager.LoadScene("Level1");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UIManager] Failed to load scene 'Level1': {e.Message}\n" +
                           "Add 'Level1' to Build Settings (File > Build Settings > Add Open Scenes) " +
                           "or create a placeholder scene at Assets/Scenes/Level1.unity.");
        }
    }

    private void OnOptionsClicked()
    {
        if (optionsPanel == null)
        {
            Debug.LogError("[UIManager] OnOptionsClicked: optionsPanel reference is null. " +
                           "Panel cannot be toggled.");
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
        // Direct master volume — hook into an AudioMixer for more control.
        AudioListener.volume = value;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates the on-screen score display.
    /// Safe to call from any game system; logs an error if the Text reference is lost.
    /// </summary>
    public void UpdateScore(int newScore)
    {
        if (scoreText == null)
        {
            Debug.LogError("[UIManager] UpdateScore called but scoreText reference is null. " +
                           "The ScoreText UI element may have been destroyed or was never found.");
            return;
        }

        scoreText.text = "Score: " + newScore;
    }

    /// <summary>
    /// Updates the element name label and panel colour tint.
    /// Silently skips if the UI element was not found at Start.
    /// </summary>
    public void UpdateElementDisplay(ElementStats stats)
    {
        if (elementDisplayText != null)
            elementDisplayText.text = stats.elementName;

        if (elementDisplayImage != null)
            elementDisplayImage.color = new Color(
                stats.elementColor.r,
                stats.elementColor.g,
                stats.elementColor.b,
                0.85f);  // semi-transparent background tint
    }

    /// <summary>
    /// Updates the dash charge counter (e.g. "●●○" for 2/3).
    /// </summary>
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

    /// <summary>
    /// Sets the dash charge fill bar (0 = empty, 1 = full).
    /// Called every frame while the player holds the dash button.
    /// </summary>
    public void UpdateDashChargeMeter(float normalizedCharge)
    {
        if (dashChargeMeter == null) return;
        dashChargeMeter.value = Mathf.Clamp01(normalizedCharge);
    }
}
