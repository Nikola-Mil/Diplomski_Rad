// EnemyProjectile.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices:
//   • Target direction is captured at spawn time (frozen vector), so the
//     projectile travels in a straight line rather than homing.  This gives
//     the player a predictable, dodge-able threat.
//   • Water element's phaseThroughProjectiles is respected via
//     PlayerController.IsDashingWithPhaseProjectiles: if true, the projectile
//     passes through without dealing damage or destroying itself.
//   • Rigidbody2D with gravityScale=0 drives movement so the physics engine
//     handles wall/ground collision naturally (non-trigger colliders stop it).
//   • Lifetime auto-destroy prevents orphaned projectiles if the player
//     runs out of range.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • Initialize not called before Start: direction is derived from player
//     position at Start time; falls back to Vector2.left if player not found.
//   • No Rigidbody2D at Start: transform-based movement fallback is used with
//     a warning.
//   • Player found but no PlayerController: logged as warning, projectile
//     destroys itself to avoid cluttering the scene.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

public class EnemyProjectile : MonoBehaviour
{
    [Tooltip("World units per second.")]
    public float speed    = 5f;
    [Tooltip("Seconds before the projectile auto-destroys.")]
    public float lifetime = 5f;
    [Tooltip("Damage dealt to the player on contact.")]
    public int   damage   = 1;

    // ── Private state ────────────────────────────────────────────────────────
    private Vector2      moveDirection;
    private bool         initialized;
    private float        timer;
    private Rigidbody2D  rb;

    // ── Public initialisation ────────────────────────────────────────────────

    /// <summary>
    /// Called by EnemyPatrol immediately after instantiation.
    /// Passing the direction here (rather than computing it in Start) ensures
    /// the target position is locked to the moment of fire, not the moment
    /// the MonoBehaviour Start runs (which may be a frame later).
    /// </summary>
    public void Initialize(Vector2 direction)
    {
        if (direction.magnitude < 0.01f)
        {
            Debug.LogWarning("[EnemyProjectile] Initialize called with near-zero direction. " +
                             "Defaulting to Vector2.left.");
            direction = Vector2.left;
        }
        moveDirection = direction.normalized;
        initialized   = true;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogWarning("[EnemyProjectile] No Rigidbody2D found \u2013 falling back to " +
                             "transform-based movement. Add Rigidbody2D(gravityScale=0) for " +
                             "proper physics collision with walls.");
        }
        else
        {
            rb.gravityScale = 0f;
        }

        // If EnemyPatrol did not call Initialize (e.g. manually placed in scene),
        // derive direction from the player position at this moment.
        if (!initialized)
        {
            var playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null)
            {
                moveDirection = ((Vector2)(playerGO.transform.position - transform.position)).normalized;
                initialized   = true;
            }
            else
            {
                Debug.LogWarning("[EnemyProjectile] Initialize was not called and no 'Player' " +
                                 "found \u2013 projectile will move left by default.");
                moveDirection = Vector2.left;
                initialized   = true;
            }
        }

        if (rb != null)
            rb.linearVelocity = moveDirection * speed;
    }

    private void Update()
    {
        // Auto-destroy after lifetime expires.
        timer += Time.deltaTime;
        if (timer >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        // Fallback transform movement when no Rigidbody2D is present.
        if (rb == null)
            transform.position += (Vector3)(moveDirection * speed * Time.deltaTime);
    }

    // ── Collision ─────────────────────────────────────────────────────────────

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null) return;

        if (other.CompareTag("Player"))
        {
            var pc = other.GetComponent<PlayerController>();
            if (pc == null)
            {
                Debug.LogWarning($"[EnemyProjectile] Hit 'Player'-tagged '{other.gameObject.name}' " +
                                 "but no PlayerController found. Projectile destroyed.");
                Destroy(gameObject);
                return;
            }

            // Water element: if the player is mid-dash with phaseThroughProjectiles,
            // this hit is completely ignored and the projectile keeps flying.
            // This is Water's specific mechanic: dashing through bullet
            // patterns risk-free, without even breaking the projectile.
            if (pc.IsDashingWithPhaseProjectiles)
            {
                Debug.Log("[EnemyProjectile] Water dash \u2013 projectile phased through.");
                return; // do not consume projectile; it keeps flying
            }

            // General dash i-frames (any element): the player takes no damage
            // while dashing, matching the classic "invincible during your
            // dash" feel. Unlike Water above, this still consumes/destroys
            // the projectile \u2014 the player tanks it rather than phasing
            // through it, since only Water actually ignores the hit outright.
            if (pc.IsDashing())
            {
                Debug.Log("[EnemyProjectile] Player is dashing \u2013 hit ignored (i-frames), no damage.");
                Destroy(gameObject);
                return;
            }

            pc.TakeDamage(damage);
            Destroy(gameObject);
        }
        else if (!other.isTrigger)
        {
            // Hit a solid wall, floor, or platform tile \u2013 remove the projectile.
            Destroy(gameObject);
        }
    }
}
