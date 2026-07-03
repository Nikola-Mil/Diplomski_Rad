// WindZoneController.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices:
//   • Coroutine-based cycle keeps timing decoupled from Update physics.
//     The coroutine uses yield return null (frame-by-frame) during active phases
//     so wind force is applied every FixedUpdate-aligned frame.
//   • deterministic=true means durations are fixed; false adds a random cooldown
//     variation so repeated plays don't feel mechanical.
//   • Warning phase applies 30 % wind to the scarf (visual cue).
//     Full phase applies 100 % wind to scarf AND an AddForce impulse to the player.
//   • Player position is captured at the START of the warning (inhale) phase so
//     the fireball targets where the player was, not where they dodge to.
//   • DragonPosition orbits an off-screen centre in Update — independent of the
//     wind cycle.  No SpriteRenderer; it's a positional anchor only.
//   • References (Player, ScarfController) are re-resolved each cycle so the
//     system survives scene reloads or late-spawned objects.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • Player not found on a cycle: logs an error and skips AddForce / fireball.
//     Will retry next cycle automatically.
//   • ScarfController not found: logs and skips wind-on-scarf.  Retries next cycle.
//   • DragonPosition child missing: logs and skips fireball spawn.
//   • Fireball pool exhausted (> maxFireballs active): oldest is destroyed first.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WindZoneController : MonoBehaviour
{
    // ── Public configuration ─────────────────────────────────────────────────
    [Header("Wind Direction & Strength")]
    public Vector2 windDirection    = Vector2.right;
    public float   maxStrength      = 8f;

    [Header("Cycle Durations (seconds)")]
    public float warningDuration  = 1.5f;
    public float fullDuration     = 2.5f;
    public float cooldownDuration = 3f;

    [Header("Behaviour")]
    [Tooltip("True = exact durations every cycle.  False = random cooldown variation.")]
    public bool deterministic = true;

    [Header("Dragon Orbit")]
    [Tooltip("Centre of the off-screen orbit.")]
    public Vector3 dragonOrbitCenter = new Vector3(0f, 18f, 0f);
    public float   dragonOrbitRadius = 6f;
    public float   dragonOrbitSpeed  = 1f;

    [Header("Fireball")]
    public float  fireballSpeed    = 9f;
    public float  fireballLifetime = 6f;
    public int    maxFireballs     = 4;

    // ── Private state ────────────────────────────────────────────────────────
    private Transform      dragonTransform;
    private Rigidbody2D    playerRb;
    private Transform      playerTransform;
    private PlayerController playerController;
    private ScarfController scarfController;
    private UIManager        uiManager;

    private readonly Queue<GameObject> activeFireballs = new Queue<GameObject>();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        // Locate the DragonPosition child
        var dragonGO = FindChildByName("DragonPosition");
        if (dragonGO == null)
        {
            Debug.LogError("[WindZoneController] 'DragonPosition' child not found. " +
                           "Fireball spawning will be skipped until it is present. " +
                           "Ensure BuildGameScene has been run.");
        }
        else
        {
            dragonTransform = dragonGO.transform;
        }

        // Normalise wind direction to prevent magnitude from affecting strength
        if (windDirection == Vector2.zero)
        {
            Debug.LogWarning("[WindZoneController] windDirection is zero-vector; defaulting to Vector2.right.");
            windDirection = Vector2.right;
        }
        windDirection = windDirection.normalized;

        StartCoroutine(WindCycle());
    }

    private void Update()
    {
        // Dragon orbits off-screen, independent of the wind cycle phase.
        if (dragonTransform != null)
        {
            float angle = Time.time * dragonOrbitSpeed;
            dragonTransform.position = dragonOrbitCenter + new Vector3(
                Mathf.Cos(angle) * dragonOrbitRadius,
                Mathf.Sin(angle) * dragonOrbitRadius,
                0f
            );
        }
    }

    // ── Wind cycle coroutine ─────────────────────────────────────────────────

    private IEnumerator WindCycle()
    {
        while (true)
        {
            // ── Cooldown ──────────────────────────────────────────────────────
            float cd = deterministic
                ? cooldownDuration
                : cooldownDuration + UnityEngine.Random.Range(0f, 2f);
            yield return new WaitForSeconds(cd);

            // Refresh scene references each cycle (robust to late spawns / reloads)
            RefreshReferences();

            // Capture player position at inhale start (fireball target)
            Vector3 capturedPlayerPos = playerTransform != null
                ? playerTransform.position
                : new Vector3(0f, -3f, 0f); // fallback centre of level

            // ── Warning phase ─────────────────────────────────────────────────
            uiManager?.ShowWindIndicator(true, windDirection * 0.3f);
            float elapsed = 0f;
            while (elapsed < warningDuration)
            {
                elapsed += Time.deltaTime;

                if (scarfController != null)
                    scarfController.ApplyWind(windDirection * maxStrength * 0.3f);

                yield return null;
            }

            // ── Full wind phase ───────────────────────────────────────────────
            uiManager?.ShowWindIndicator(true, windDirection);
            elapsed = 0f;
            while (elapsed < fullDuration)
            {
                elapsed += Time.deltaTime;

                // Skip while dashing OR still coasting on the post-dash momentum
                // carry-over (IsDashingOrCoasting). During a dash, gravity is
                // zeroed and movement is driven entirely by MovePosition — an
                // AddForce here has nothing opposing it and just silently
                // accumulates in the rigidbody's velocity. Right after the dash,
                // horizontal deceleration is deliberately suppressed for a
                // moment (dashMomentumTimer) so the dash's own momentum carries
                // through — but that same suppression means wind's push isn't
                // being cancelled out either, so it also piles onto the
                // momentum instead of being felt as a normal, gentle nudge.
                bool dashing = playerController != null && playerController.IsDashingOrCoasting();
                if (playerRb != null && !dashing)
                    playerRb.AddForce(windDirection * maxStrength, ForceMode2D.Force);
                else if (playerTransform == null)
                    Debug.LogError("[WindZoneController] Player Rigidbody2D is null during full wind phase. " +
                                   "Player may have been destroyed.");

                if (scarfController != null)
                    scarfController.ApplyWind(windDirection * maxStrength);

                yield return null;
            }

            uiManager?.ShowWindIndicator(false);

            // ── Fireball attack ───────────────────────────────────────────────
            SpawnFireball(capturedPlayerPos);
        }
    }

    // ── Reference resolution ─────────────────────────────────────────────────

    private void RefreshReferences()
    {
        if (uiManager == null)
            uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();

        // Player
        if (playerTransform == null)
        {
            var playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO == null)
            {
                Debug.LogError("[WindZoneController] No GameObject tagged 'Player' found in scene. " +
                               "Wind force and fireball will be skipped this cycle.");
                playerRb        = null;
                playerTransform = null;
            }
            else
            {
                playerTransform  = playerGO.transform;
                playerRb         = playerGO.GetComponent<Rigidbody2D>();
                playerController = playerGO.GetComponent<PlayerController>();
                if (playerRb == null)
                    Debug.LogError("[WindZoneController] Player has no Rigidbody2D – " +
                                   "wind AddForce will be skipped.");
            }
        }

        // ScarfController (on the ScarfRoot child of Player)
        if (scarfController == null)
        {
            scarfController = UnityEngine.Object.FindFirstObjectByType<ScarfController>();
            if (scarfController == null)
                Debug.LogError("[WindZoneController] ScarfController not found in scene. " +
                               "Scarf wind will be skipped this cycle.");
        }
    }

    // ── Fireball spawning ─────────────────────────────────────────────────────

    private void SpawnFireball(Vector3 targetPosition)
    {
        if (dragonTransform == null)
        {
            Debug.LogError("[WindZoneController] Cannot spawn fireball – DragonPosition transform is null.");
            return;
        }

        // Enforce fireball cap
        while (activeFireballs.Count >= maxFireballs)
        {
            var oldest = activeFireballs.Dequeue();
            if (oldest != null) Destroy(oldest);
        }

        try
        {
            var fbGO = new GameObject("Fireball");
            fbGO.transform.position = dragonTransform.position;

            var sr    = fbGO.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCircleSprite(new Color(1f, 0.6f, 0.1f));
            sr.color  = new Color(1f, 0.55f, 0.1f);
            fbGO.transform.localScale = Vector3.one * 0.4f;

            var rb = fbGO.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType     = RigidbodyType2D.Dynamic;

            Vector2 direction = ((Vector2)targetPosition - (Vector2)dragonTransform.position);
            if (direction.magnitude < 0.01f) direction = Vector2.down; // degenerate guard
            direction = direction.normalized;

            var fb = fbGO.AddComponent<FireballBehavior>();
            fb.moveVelocity = direction * fireballSpeed;
            fb.lifetime     = fireballLifetime;

            activeFireballs.Enqueue(fbGO);
        }
        catch (Exception e)
        {
            Debug.LogError($"[WindZoneController] Fireball spawn failed: {e.Message}\n{e.StackTrace}");
        }
    }

    // ── Utilities ────────────────────────────────────────────────────────────

    private GameObject FindChildByName(string childName)
    {
        foreach (Transform child in transform)
            if (child.name == childName) return child.gameObject;
        return null;
    }

    private static Sprite CreateCircleSprite(Color color)
    {
        const int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                tex.SetPixel(x, y, dx * dx + dy * dy <= r * r ? color : Color.clear);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size),
                             new Vector2(0.5f, 0.5f), size);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// FireballBehavior – self-contained mover attached to each fireball GameObject.
// Kept in the same file to avoid a separate compilation unit for a minor helper.
// ─────────────────────────────────────────────────────────────────────────────
public class FireballBehavior : MonoBehaviour
{
    public Vector2 moveVelocity;
    public float   lifetime = 6f;

    private Rigidbody2D rb;
    private float       timer;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogError("[FireballBehavior] Rigidbody2D missing on fireball – " +
                           "adding one now. Fireball will move correctly.");
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
        }
        rb.linearVelocity = moveVelocity;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= lifetime)
            Destroy(gameObject);
    }
}
