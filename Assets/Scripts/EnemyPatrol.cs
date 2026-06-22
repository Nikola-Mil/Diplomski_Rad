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

    // ── Shooting ────────────────────────────────────────────────────────────
    [Header("Shooting")]
    [Tooltip("Seconds between shots. Set 0 to disable shooting entirely.")]
    public float     shootInterval   = 3f;
    [Tooltip("Player must be within this distance for the enemy to fire.")]
    public float     shootRange      = 10f;
    [Tooltip("Prefab with EnemyProjectile component. Null = auto-create a primitive.")]
    public GameObject projectilePrefab;

    // ── Private state ─────────────────────────────────────────────────────────
    private EnemyState  state        = EnemyState.Walk;
    private float       waitTimer;
    private int         moveDirection = 1; // +1 = right, -1 = left
    private Rigidbody2D rb;
    private Collider2D  col;

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
                rb.MovePosition(rb.position + knockbackVelocity * Time.fixedDeltaTime);
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
            // Placeholder: replace with a proper damage/health system call.
            Debug.Log($"[EnemyPatrol] '{gameObject.name}' contacted Player – Damage!");
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
    /// Called by PlayerController when an Earth or Fire dash makes contact.
    /// Knockback is stored as a timed velocity since kinematic bodies ignore AddForce.
    /// </summary>
    public void TakeDamage(float damage, Vector2 knockback)
    {
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
            Destroy(gameObject);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

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
