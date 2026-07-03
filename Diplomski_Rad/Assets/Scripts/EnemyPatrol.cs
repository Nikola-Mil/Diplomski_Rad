// EnemyPatrol.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices:
//   • Simple two-state FSM (Walk / Wait) keeps the logic readable and easy to
//     extend.  Enums are preferred over magic booleans/integers.
//   • Kinematic Rigidbody2D movement uses MovePosition so the physics engine
//     resolves collisions correctly; directly setting transform.position would
//     tunnel through thin colliders at high speeds.
//   • waitTime gives the enemy a brief pause at each patrol bound — looks more
//     natural than an instant direction reversal.
//   • OnTriggerEnter2D relies on the "Player" tag rather than a direct script
//     reference so it works even if PlayerController is not yet loaded.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • Missing Collider2D at Awake: added automatically with a warning.
//   • leftBound >= rightBound: clamped and warned so the patrol range is valid.
//   • Missing Rigidbody2D: added as Kinematic and warned.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections;
using UnityEngine;

public class EnemyPatrol : MonoBehaviour
{
    // ── FSM states ────────────────────────────────────────────────────────────
    private enum EnemyState { Walk, Wait }

    // ── Public configuration ──────────────────────────────────────────────────
    [Header("Patrol")]
    public float moveSpeed  = 2f;
    public float leftBound  = -2f;
    public float rightBound = 2f;
    [Tooltip("Seconds the enemy pauses at each bound before turning.")]
    public float waitTime   = 1f;

    // ── Health ────────────────────────────────────────────────────────────────
    [Header("Health")]
    [Tooltip("Hit points. Set <= 0 to make enemy unkillable.")]
    public float health = 20f;

    [Header("Health Bar")]
    [Tooltip("Local offset above the enemy sprite where the health bar is drawn.")]
    public Vector2 healthBarOffset = new Vector2(0f, 0.9f);
    [Tooltip("World-unit size of the health bar background.")]
    public Vector2 healthBarSize   = new Vector2(0.9f, 0.14f);

    // ── Shooting ────────────────────────────────────────────────────────────
    [Header("Shooting")]
    [Tooltip("Seconds between shots. Set 0 to disable shooting entirely.")]
    public float     shootInterval   = 3f;
    [Tooltip("Player must be within this distance for the enemy to fire.")]
    public float     shootRange      = 10f;
    [Tooltip("Prefab with EnemyProjectile component. Null = auto-create a primitive.")]
    public GameObject projectilePrefab;

    [Header("Debug")]
    [Tooltip("FOR DEBUGGING PURPOSES ONLY: seconds after death before the enemy respawns at its " +
             "starting position with full health, instead of staying dead. Lets combat be re-tested " +
             "repeatedly without reloading/rebuilding the scene. Set <= 0 to disable and get a real, " +
             "permanent death — turn this off before anything resembling a real playtest or build.")]
    public float debugRespawnDelay = 3f;

    // ── Private state ─────────────────────────────────────────────────────────
    private EnemyState    state        = EnemyState.Walk;
    private float         waitTimer;
    private int            moveDirection = 1; // +1 = right, -1 = left
    private Rigidbody2D    rb;
    private Collider2D     col;
    private SpriteRenderer sr;
    private Vector3        spawnPosition;
    private bool           isDead;

    // Snapshot of the starting health value, used as the "full" reference for
    // the health bar fraction and for Water's "half of max health" damage —
    // reading the current (already-reduced) health for that instead would
    // make a second hit deal less than the first and never finish the kill.
    private float maxHealth;
    public  float MaxHealth => maxHealth;

    // Health bar — always visible while alive (health > 0), hidden on death.
    private GameObject      healthBarRoot;
    private Transform       healthBarFillTransform;

    // Knockback: stored as timed velocity override (kinematic bodies ignore AddForce)
    private Vector2 knockbackVelocity;
    private float   knockbackTimer;
    private const float KNOCKBACK_DURATION = 0.2f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogWarning("[EnemyPatrol] Rigidbody2D missing on '" + gameObject.name +
                             "' – adding a Kinematic body automatically.");
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        col = GetComponent<Collider2D>();
        if (col == null)
        {
            Debug.LogWarning("[EnemyPatrol] Collider2D missing on '" + gameObject.name +
                             "' – adding a BoxCollider2D automatically. " +
                             "Resize it in the Inspector to match the sprite.");
            col = gameObject.AddComponent<BoxCollider2D>();
            ((BoxCollider2D)col).isTrigger = true;
        }

        sr = GetComponent<SpriteRenderer>();

        // Validate patrol bounds
        if (leftBound >= rightBound)
        {
            Debug.LogWarning($"[EnemyPatrol] leftBound ({leftBound}) >= rightBound ({rightBound}) " +
                             "on '" + gameObject.name + "'. Clamping: leftBound = rightBound - 1.");
            leftBound = rightBound - 1f;
        }

        // Snap to the left bound at start to normalise position
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, leftBound, rightBound);
        transform.position = pos;
        spawnPosition = pos;

        maxHealth = health;
        SetupHealthBar();
    }

    private void Start()
    {
        // Start the shooting coroutine only if shooting is enabled.
        if (shootInterval > 0f)
            StartCoroutine(ShootingCoroutine());
    }

    private void Update()
    {
        try
        {
            UpdateFSM();
        }
        catch (Exception e)
        {
            Debug.LogError($"[EnemyPatrol] Exception in Update on '{gameObject.name}': " +
                           $"{e.Message}\n{e.StackTrace}");
        }
    }

    private void FixedUpdate()
    {
        // Apply knockback override; skip normal patrol while knocked.
        if (knockbackTimer > 0f)
        {
            if (rb != null)
            {
                Vector2 delta = knockbackVelocity * Time.fixedDeltaTime;
                float   dist  = delta.magnitude;
                if (dist > 0.0001f)
                {
                    Vector2 dir = delta / dist;
                    // Kinematic bodies aren't stopped by Unity's collision
                    // resolution — MovePosition would otherwise drive a strong
                    // knockback (e.g. Water's dash-end push) straight through
                    // solid ground/walls. Cast ahead along the knockback path
                    // and clamp to the first solid (non-trigger) hit instead.
                    var hits = Physics2D.CircleCastAll(rb.position, 0.4f, dir, dist);
                    float clampedDist = dist;
                    bool  hitSolid    = false;
                    foreach (var hit in hits)
                    {
                        if (hit.collider == null)                  continue;
                        if (hit.collider.gameObject == gameObject) continue;
                        if (hit.collider.isTrigger)                continue; // other enemies, player, pickups
                        if (hit.distance < clampedDist)
                        {
                            clampedDist = hit.distance;
                            hitSolid    = true;
                        }
                    }
                    rb.MovePosition(rb.position + dir * clampedDist);
                    if (hitSolid) knockbackTimer = 0f; // stop early instead of pushing into the wall
                }
            }
            knockbackTimer -= Time.fixedDeltaTime;
            return;
        }

        if (state != EnemyState.Walk) return;

        if (rb == null)
        {
            Debug.LogError("[EnemyPatrol] Rigidbody2D is null in FixedUpdate – movement skipped.");
            return;
        }

        float newX = transform.position.x + moveDirection * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(new Vector2(newX, transform.position.y));
    }

    // ── FSM update ────────────────────────────────────────────────────────────

    private void UpdateFSM()
    {
        switch (state)
        {
            case EnemyState.Walk:
                CheckBounds();
                break;

            case EnemyState.Wait:
                waitTimer -= Time.deltaTime;
                if (waitTimer <= 0f)
                {
                    moveDirection = -moveDirection;
                    FlipSprite();
                    state = EnemyState.Walk;
                }
                break;
        }
    }

    private void CheckBounds()
    {
        float x = transform.position.x;
        if ((moveDirection > 0 && x >= rightBound) ||
            (moveDirection < 0 && x <= leftBound))
        {
            // Clamp to bound to avoid overshot drift
            Vector3 pos = transform.position;
            pos.x = moveDirection > 0 ? rightBound : leftBound;
            transform.position = pos;

            waitTimer = waitTime;
            state     = EnemyState.Wait;
        }
    }

    private void FlipSprite()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.flipX = moveDirection < 0;
    }

    // ── Collision ─────────────────────────────────────────────────────────────

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null)
        {
            Debug.LogError("[EnemyPatrol] OnTriggerEnter2D received a null Collider2D – " +
                           "this should not happen in normal Unity execution.");
            return;
        }

        if (other.CompareTag("Player"))
        {
            var pc = other.GetComponent<PlayerController>();
            if (pc == null)
            {
                Debug.LogWarning($"[EnemyPatrol] '{gameObject.name}' contacted 'Player' but no PlayerController found.");
                return;
            }

            // While dashing, all enemy interaction is already handled by the
            // dash's own hit detection (PlayerController.HandleEnemyContact —
            // Earth/Fire damage+stop, Water/Air pass-through, plus dash
            // i-frames matching the projectile behaviour). This trigger firing
            // on top of that as ordinary "walked into an enemy" contact damage
            // was dealing damage the player shouldn't be taking mid-dash at
            // all, regardless of which element is active.
            if (pc.IsDashing())
                return;

            pc.TakeDamage(1);
        }
    }

    // ── Shooting coroutine ───────────────────────────────────────────────────────

    private IEnumerator ShootingCoroutine()
    {
        // Stagger the first shot so enemies on the same screen don't fire together.
        yield return new WaitForSeconds(UnityEngine.Random.Range(0f, shootInterval));

        while (true)
        {
            yield return new WaitForSeconds(shootInterval);

            if (isDead) continue; // mid debug-respawn delay — don't shoot while "dead"

            var playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO == null) continue; // player not present; skip silently

            float dist = Vector2.Distance(transform.position, playerGO.transform.position);
            if (dist > shootRange) continue;

            try
            {
                FireAt(playerGO.transform.position);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EnemyPatrol] FireAt threw on '{gameObject.name}': {e.Message}");
            }
        }
    }

    private void FireAt(Vector3 targetPosition)
    {
        Vector2 direction = ((Vector2)(targetPosition - transform.position)).normalized;

        GameObject proj;
        if (projectilePrefab != null)
        {
            proj = Instantiate(projectilePrefab, transform.position, Quaternion.identity);
        }
        else
        {
            Debug.LogWarning($"[EnemyPatrol] projectilePrefab null on '{gameObject.name}' – " +
                             "creating a runtime circle projectile.");
            proj = CreateRuntimeProjectile();
        }

        if (proj == null)
        {
            Debug.LogError("[EnemyPatrol] FireAt: projectile GameObject could not be created.");
            return;
        }

        var ep = proj.GetComponent<EnemyProjectile>();
        if (ep == null) ep = proj.AddComponent<EnemyProjectile>();
        ep.Initialize(direction);
    }

    private GameObject CreateRuntimeProjectile()
    {
        var go = new GameObject("EnemyProjectile");
        go.transform.position   = transform.position;
        go.transform.localScale = Vector3.one * 0.25f;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = CreateCircleSprite(new Color(1f, 0.35f, 0.05f));

        var projRb = go.AddComponent<Rigidbody2D>();
        projRb.gravityScale = 0f;

        var cc = go.AddComponent<CircleCollider2D>();
        cc.isTrigger = true;

        go.AddComponent<EnemyProjectile>();
        return go;
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by PlayerController for any elemental dash contact — damage and
    /// knockback amounts vary per element (see HandleEnemyContact); a call with
    /// damage 0 is valid and used for pure-knockback pushes (e.g. Water/Air
    /// shoving an enemy the dash ended on). Knockback is stored as a timed
    /// velocity since kinematic bodies ignore AddForce.
    /// </summary>
    public void TakeDamage(float damage, Vector2 knockback)
    {
        if (isDead) return; // already dead / mid debug-respawn delay — ignore further hits

        if (damage < 0f)
        {
            Debug.LogWarning($"[EnemyPatrol] TakeDamage received negative damage ({damage}). " +
                             "Value must be positive; call ignored.");
            return;
        }

        health -= damage;
        Debug.Log($"[EnemyPatrol] '{gameObject.name}' took {damage} damage. HP: {health}.");

        if (knockback.magnitude > 0.01f)
        {
            knockbackVelocity = knockback;
            knockbackTimer    = KNOCKBACK_DURATION;
        }

        if (health <= 0f)
        {
            Debug.Log($"[EnemyPatrol] '{gameObject.name}' defeated.");
            isDead = true;
            if (healthBarRoot != null) healthBarRoot.SetActive(false);
            if (debugRespawnDelay > 0f)
                StartCoroutine(DebugRespawnAfterDelay());
            else
                Destroy(gameObject);
            return;
        }

        // Always visible while alive, reflecting current HP fraction.
        RefreshHealthBar();
    }

    // ── DEBUG respawn ────────────────────────────────────────────────────────
    // FOR DEBUGGING PURPOSES ONLY. Lets combat (damage values, AoE radii,
    // knockback, the health bar) be re-tested over and over without reloading
    // or rebuilding the scene every time an enemy dies. Set debugRespawnDelay
    // <= 0 to get a real, permanent death instead.

    private IEnumerator DebugRespawnAfterDelay()
    {
        // Disable visuals/collision/shooting but keep the GameObject (and this
        // coroutine) active — SetActive(false) would also pause the coroutine
        // that's supposed to bring it back.
        if (sr  != null) sr.enabled  = false;
        if (col != null) col.enabled = false;
        state = EnemyState.Wait;

        yield return new WaitForSeconds(debugRespawnDelay);

        health        = maxHealth;
        isDead        = false;
        moveDirection = 1;
        state         = EnemyState.Walk;
        transform.position = spawnPosition;

        if (sr  != null) sr.enabled  = true;
        if (col != null) col.enabled = true;

        RefreshHealthBar(); // health == maxHealth again, bar shows full
        Debug.Log($"[EnemyPatrol] '{gameObject.name}' respawned (DEBUG — disable via debugRespawnDelay).");
    }

    // ── Health bar ───────────────────────────────────────────────────────────

    private void SetupHealthBar()
    {
        healthBarRoot = new GameObject("HealthBarRoot");
        healthBarRoot.transform.SetParent(transform, false);
        healthBarRoot.transform.localPosition = healthBarOffset;
        healthBarRoot.SetActive(true);

        var bgGO = new GameObject("HealthBarBG");
        bgGO.transform.SetParent(healthBarRoot.transform, false);
        var bgSr = bgGO.AddComponent<SpriteRenderer>();
        bgSr.sprite       = CreateCenteredBarSprite(new Color(0.05f, 0.05f, 0.05f, 0.85f));
        bgSr.sortingOrder = 5;
        bgGO.transform.localScale = new Vector3(healthBarSize.x, healthBarSize.y, 1f);

        // Left-pivoted sprite: scaling localScale.x shrinks the bar from the
        // right while its left edge stays anchored, exactly like a normal
        // health bar depleting rather than shrinking from the centre.
        var fillGO = new GameObject("HealthBarFill");
        fillGO.transform.SetParent(healthBarRoot.transform, false);
        fillGO.transform.localPosition = new Vector3(-healthBarSize.x * 0.5f, 0f, -0.01f);
        var fillSr = fillGO.AddComponent<SpriteRenderer>();
        fillSr.sprite       = CreateLeftPivotBarSprite(new Color(0.9f, 0.15f, 0.15f));
        fillSr.sortingOrder = 6;
        healthBarFillTransform = fillGO.transform;
        healthBarFillTransform.localScale = new Vector3(healthBarSize.x, healthBarSize.y, 1f);
    }

    private void RefreshHealthBar()
    {
        if (healthBarRoot == null) return;

        bool showBar = health > 0f;
        healthBarRoot.SetActive(showBar);
        if (!showBar) return;

        float fraction = maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;
        healthBarFillTransform.localScale = new Vector3(healthBarSize.x * fraction, healthBarSize.y, 1f);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Solid-colour, centre-pivoted, 1x1-world-unit-native sprite — for the
    /// health bar background. Square texture with PPU == its own pixel size is
    /// deliberate: it makes the sprite's native size exactly (1,1), so the
    /// localScale applied in SetupHealthBar/RefreshHealthBar maps 1:1 to the
    /// final world size instead of getting cross-multiplied by a non-square
    /// native aspect ratio (a 16x4 sprite at PPU 16 has native size (1, 0.25),
    /// which silently squashed the bar to a quarter of its intended height).
    /// </summary>
    private static Sprite CreateCenteredBarSprite(Color color)
    {
        const int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>Same as CreateCenteredBarSprite but LEFT-pivoted — for the health bar fill (see RefreshHealthBar).</summary>
    private static Sprite CreateLeftPivotBarSprite(Color color)
    {
        const int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0f, 0.5f), size);
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
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
