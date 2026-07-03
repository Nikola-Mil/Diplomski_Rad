// PlayerController.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices:
//   • Physics (velocity write) is performed in FixedUpdate so it stays
//     frame-rate independent; input sampling is in Update for responsiveness.
//   • Coyote time: the player may still jump for a short window after walking
//     off a ledge.  This is industry-standard and dramatically reduces the
//     feeling of "sticky" edges.
//   • Jump buffer: if the player presses Jump slightly before landing, the
//     intent is stored and consumed on the first grounded frame, removing
//     the frustration of missed inputs.
//   • Input System duality: when the project has the new Input System package
//     enabled, InputAction objects are created entirely in code (no .inputactions
//     asset required).  The legacy path is an unconditional fallback via #else.
//   • linearVelocity is used (Unity 6+).  Replace with velocity for older versions
//     if a deprecation warning appears.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • Missing Rigidbody2D at Awake: component is added automatically with a warning.
//   • Missing BoxCollider2D at Awake: added automatically with a warning.
//   • Ground layer mask not set: IsGrounded always returns false; coyote time
//     prevents any jump at all.  Assign the "Ground" layer in the Inspector.
//   • New Input System package missing but ENABLE_INPUT_SYSTEM is defined:
//     compilation will fail with a clear error at the using directive.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// ─────────────────────────────────────────────────────────────────────────────
// ElementStats — governs how each element modifies the dash.
//
// Risk / reward design philosophy:
//   Earth  – slow short dash, very high damage + knockback. Stops on enemy
//            contact. Combat style: controlled, punishes crowds.
//            Risk: slow speed means enemies can sidestep. Reward: best CC.
//
//   Fire   – mid speed / range, high damage, stops on hit.
//            All-round option — good starting pick.
//
//   Water  – mid speed, ZERO damage, phases through enemy projectiles.
//            Speedrunner / evasion style. Makes bullet-hell sections trivial.
//            Risk: zero offensive value. Reward: safest through dense fire.
//
//   Air    – fastest, longest range, zero damage, phases through enemies.
//            Pure traversal pick. Treats the dash as a movement tool only.
//            Risk: no combat utility. Reward: lets skilled players skip rooms.
// ─────────────────────────────────────────────────────────────────────────────
[System.Serializable]
public struct ElementStats
{
    [Tooltip("Display name shown in the HUD.")]
    public string elementName;

    [Tooltip("Dash velocity in world units/s.")]
    public float dashSpeed;

    [Tooltip("Maximum distance covered at full charge (world units).")]
    public float dashMaxDistance;

    [Tooltip("Damage dealt to an enemy on dash contact. 0 = no damage.")]
    public float damage;

    [Tooltip("Air element: dash passes through enemy bodies (pure traversal).")]
    public bool phaseThroughEnemies;

    [Tooltip("Water element: dash ignores enemy projectiles mid-flight.")]
    public bool phaseThroughProjectiles;

    [Tooltip("Impulse applied to a struck enemy. Earth = very high for crowd control.")]
    public float enemyKnockback;

    [Tooltip("Colour used for the HUD tint and the trajectory preview line.")]
    public Color elementColor;
}

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    // ── Movement ──────────────────────────────────────────────────────────────
    [Header("Movement")]
    public float moveSpeed           = 13f;
    // Paired with the gravityScale bump on the Rigidbody2D (see BuildGameScene.cs):
    // higher gravity + higher jumpForce keeps roughly the same jump height while
    // roughly halving hang time — the jump no longer feels like it "floats" at the top.
    public float jumpForce           = 17f;
    [Tooltip("Acceleration on the ground (units/s²).")]
    public float groundAcceleration  = 150f;
    [Tooltip("Deceleration on the ground when there is no input (units/s²).")]
    public float groundDeceleration  = 150f;
    [Tooltip("Acceleration in the air — lower value preserves momentum from dashes.")]
    public float airAcceleration     = 85f;
    [Tooltip("Deceleration in the air when there is no input.")]
    public float airDeceleration     = 50f;

    [Header("Variable Gravity")]
    [Tooltip("Gravity multiplier while the player is falling — makes the drop faster.")]
    public float fallGravityMultiplier = 2.5f;
    [Tooltip("Upward speed below which the apex gravity reduction activates.")]
    public float jumpApexThreshold     = 1.2f;
    [Tooltip("Gravity multiplier at the jump apex for a floaty peak.")]
    public float jumpApexGravityMult   = 0.7f;
    [Tooltip("Fraction of upward velocity kept when the jump button is released early.")]
    [Range(0f, 1f)]
    public float jumpCutMultiplier     = 0.5f;

    [Header("Ground Detection")]
    [Tooltip("Layer(s) counted as ground for IsGrounded raycasts.")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.1f;

    [Header("Feel")]
    [Tooltip("Seconds the player can still jump after leaving a ledge.")]
    public float coyoteTime          = 0.15f;
    [Tooltip("Seconds a jump press is remembered before landing.")]
    public float jumpBufferTime      = 0.15f;

    // ── Dash ──────────────────────────────────────────────────────────────────
    // No hold-to-charge: a dash always fires immediately to the element's full
    // dashMaxDistance the moment the button is pressed.
    [Header("Dash – Charges")]
    [Tooltip("Seconds after a dash ends before the next one can start.")]
    public float dashCooldown      = 0.5f;
    [Tooltip("Number of air-dashes available before landing replenishes them.")]
    public int   maxDashCharges    = 1;

    [Header("Dash – Momentum")]
    [Tooltip("Speed multiplier at the very start of the dash (>1 = a fast launch out of the gate).")]
    public float dashStartSpeedMultiplier = 1.15f;
    [Tooltip("Speed multiplier at the very end of the dash (<1 = it slows slightly as it travels). " +
             "Kept close to dashStartSpeedMultiplier so the dash itself stays fast and consistent.")]
    public float dashEndSpeedMultiplier   = 0.9f;
    [Tooltip("Fraction of the dash's final speed kept as carry-over momentum once it ends. Kept high " +
             "so the player visibly overshoots the dash distance before gravity/drag take over.")]
    [Range(0f, 1f)]
    public float dashMomentumCarryover    = 0.9f;
    [Tooltip("Seconds after a dash ends during which horizontal deceleration is almost fully " +
             "suppressed, so momentum carries through under gravity like a projectile (a parabola) " +
             "instead of being braked back to normal movement speed.")]
    public float dashMomentumHoldTime     = 0.5f;
    [Tooltip("Deceleration multiplier applied while coasting during dashMomentumHoldTime " +
             "(near-zero = momentum barely decays; gravity alone shapes the arc). Only affects " +
             "deceleration, not the player's own input.")]
    [Range(0f, 1f)]
    public float dashMomentumDecelMultiplier = 0.05f;

    [Header("Dash – Obstacle Bounce")]
    [Tooltip("Recoil speed applied opposite the dash direction when a dash is stopped early by " +
             "hitting a wall or an enemy that doesn't phase through.")]
    public float dashBounceStrength = 5f;
    [Tooltip("Seconds the bounce's deceleration is softened for — shorter than dashMomentumHoldTime " +
             "since a bounce is a quick recoil, not a sustained coast.")]
    public float dashBounceHoldTime = 0.15f;

    [Header("Dash – Elemental Combat")]
    [Tooltip("Fire: radius of the AoE explosion at the point the dash stops on an enemy. Every " +
             "enemy caught in it takes Fire's one-shot damage, not just the one directly touched.")]
    public float fireExplosionRadius     = 3.5f;
    [Tooltip("Water: radius of the small splash around the contact point. Every enemy caught in " +
             "it takes half of its OWN max health in damage.")]
    public float waterSplashRadius       = 1f;
    [Tooltip("Water/Air: knockback speed applied to an enemy still overlapping the player when " +
             "the dash reaches its full distance naturally — much stronger than the normal " +
             "enemyKnockback used for a direct Earth/Fire hit.")]
    public float dashEndEnemyPushStrength = 20f;

    [Header("Dash – Aim Visuals")]
    [Tooltip("Number of segments used to draw the max-range aim circle.")]
    public int   aimCircleSegments = 48;
    [Tooltip("Line width (world units) of the aim circle border.")]
    public float aimCircleWidth    = 0.05f;
    [Tooltip("World-unit diameter of the aim cursor dot.")]
    public float aimCursorSize     = 0.18f;

    [Header("Dash – Particles")]
    [Tooltip("Particles emitted per second while dashing, coloured per element.")]
    public float dashParticleRate  = 60f;
    [Tooltip("Particles emitted in one burst wherever the dash ends (full distance, obstacle, or interruption), coloured per element.")]
    public int   dashEndBurstCount = 24;

    // ── Element ───────────────────────────────────────────────────────────────
    [Header("Element")]
    [Tooltip("Active element – governs dash behaviour. Changed via SetElement().")]
    public ElementStats currentElement;

    // ── Health ────────────────────────────────────────────────────────────────
    [Header("Health")]
    [Tooltip("Starting health points.")]
    public int   maxHealth            = 5;
    [Tooltip("Invincibility window after taking damage (seconds).")]
    public float invincibilityDuration = 1.5f;

    // ── Private movement state ────────────────────────────────────────────────────
    private Rigidbody2D   rb;
    private BoxCollider2D col;
    private float         horizontalInput;
    private float         coyoteCounter;
    private float         jumpBufferCounter;
    private bool          wasGrounded;
    // IsGrounded() is a spatial BoxCast against the current transform position.
    // Update() can run several times between two FixedUpdate/physics steps, and
    // the transform doesn't move at all in between — caching this once per
    // FixedUpdate means every Update() call in between sees one consistent,
    // physically-accurate value instead of a stale one. This alone does NOT
    // stop a mashed double-jump, though: physics only moves the player once
    // per FixedUpdate, so "grounded" genuinely, correctly stays true for the
    // player's very own jump command over that whole physics step — a second
    // press landing inside that same still-grounded step is a second real,
    // accurately-detected "you're standing on the ground" reading. See
    // hasJumpedThisAirtime for what actually closes that gap.
    private bool          groundedCache;
    // Latches true the instant a jump (or air-dash) is consumed, false again
    // only on the next LANDING EDGE (grounded flips from false to true) — not
    // on every continuously-grounded frame. That distinction matters: "grounded"
    // stays legitimately true for the rest of the physics step a jump fires in
    // (position hasn't moved yet), so resetting this on every grounded check
    // would immediately re-arm the jump within that same step and let a mashed
    // second press through. Only a genuine landing transition re-arms it.
    private bool          hasJumpedThisAirtime;
    private float         originalGravityScale;
    // Counts down after a dash ends; while positive, FixedUpdate's deceleration
    // (not acceleration) is scaled by dashMomentumDecelMultiplier so the dash's
    // carry-over velocity actually reads as a coast instead of being cancelled
    // within a frame or two of the snappy normal deceleration values.
    private float         dashMomentumTimer;

    // ── Private dash state ────────────────────────────────────────────────────────
    private int           currentDashCharges;
    private bool          isDashing;
    private Vector2       dashDirection;
    private Vector2       dashStartPos;      // unused directly but useful for debug gizmos
    private float         dashCooldownTimer;
    private LineRenderer  aimRangeCircle;    // border-only circle, radius = max dash distance
    private SpriteRenderer aimCursor;        // dot clamped to stay inside aimRangeCircle
    private ParticleSystem dashParticles;    // coloured per currentElement, emits while dashing
    private ParticleSystem dashEndBurst;     // one-shot radial burst wherever a dash ends
    private ParticleSystem fireExplosionParticles; // bigger one-shot burst spread across fireExplosionRadius
    private ParticleSystem hitFlashParticles;      // red burst emitted whenever the player takes damage

    // ── Private health state ────────────────────────────────────────────────────
    private int           currentHealth;
    private bool          isInvincible;
    private float         invincibilityTimer;
    private Vector3       spawnPosition;

    // ── UI cache ────────────────────────────────────────────────────────────────
    private UIManager     uiManager;

#if ENABLE_INPUT_SYSTEM
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;   // left mouse / right gamepad trigger
#endif

    // ── Static element factory methods ─────────────────────────────────────────────
    // Centralised presets avoid magic numbers scattered across the project.
    // BuildGameScene.cs and ElementPickup.cs both call these.
    public static ElementStats CreateEarthElement() => new ElementStats
    {
        elementName             = "Earth",
        dashSpeed               = 6f,
        dashMaxDistance         = 4f,
        damage                  = 9999f, // one-shot kill, regardless of enemy health
        phaseThroughEnemies     = false,
        phaseThroughProjectiles = false,
        enemyKnockback          = 18f,   // high: crowd-control hammer
        elementColor            = new Color(0.3f, 0.7f, 0.2f)
    };

    // Dash speed ladder, slowest → fastest: Earth (6) < Fire (10) < Water (16) < Air (24).
    // Spread widened substantially (vs. the old 6/8/11/16) so each element's
    // dash reads as a clearly different speed at a glance, not just a slight tweak.
    public static ElementStats CreateFireElement() => new ElementStats
    {
        elementName             = "Fire",
        dashSpeed               = 10f,   // clearly faster than Earth
        dashMaxDistance         = 6f,
        damage                  = 9999f, // one-shot kill (applied AoE — see fireExplosionRadius)
        phaseThroughEnemies     = false,
        phaseThroughProjectiles = false,
        enemyKnockback          = 8f,
        elementColor            = new Color(1f, 0.3f, 0.05f)
    };

    public static ElementStats CreateWaterElement() => new ElementStats
    {
        elementName             = "Water",
        dashSpeed               = 16f,   // clearly faster than Fire
        dashMaxDistance         = 6f,
        // Unused: Water's per-hit damage is computed dynamically as half of
        // each target's OWN max health (see PerformDash) so it reliably takes
        // exactly two hits to kill regardless of a given enemy's configured
        // health, rather than a fixed number that would only line up for the
        // default 20 HP.
        damage                  = 0f,
        phaseThroughEnemies     = false,
        phaseThroughProjectiles = true,  // key Water trait: ignore incoming fire
        enemyKnockback          = 4f,
        elementColor            = new Color(0.1f, 0.5f, 1f)
    };

    public static ElementStats CreateAirElement() => new ElementStats
    {
        elementName             = "Air",
        dashSpeed               = 24f,   // fastest of all elements, by a clear margin
        dashMaxDistance         = 8f,    // longest dash range (still clearly ahead of Earth/Fire/Water)
        damage                  = 0f,    // traversal-only
        phaseThroughEnemies     = true,  // key Air trait: ghost through enemy bodies
        phaseThroughProjectiles = false,
        enemyKnockback          = 0f,
        elementColor            = new Color(0.9f, 0.9f, 1f)
    };

    /// <summary>
    /// Baseline dash used before any element has been picked up.  Shorter range
    /// than Earth (the shortest real element) so elements always feel like a
    /// clear upgrade, but moves at Fire's speed so the dash still feels usable.
    /// </summary>
    public static ElementStats CreateNeutralElement() => new ElementStats
    {
        elementName             = "None",
        dashSpeed               = 10f,   // same pace as Fire
        dashMaxDistance         = 3f,    // shorter than Earth's 4
        damage                  = 0f,
        phaseThroughEnemies     = false,
        phaseThroughProjectiles = false,
        enemyKnockback          = 0f,
        elementColor            = new Color(0.75f, 0.75f, 0.75f)
    };

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Rigidbody2D – required by [RequireComponent] but guard anyway.
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogWarning("[PlayerController] Rigidbody2D was missing on '" +
                             gameObject.name + "' – adding one now. " +
                             "Set gravityScale and constraints in the Inspector.");
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale  = 4.5f;
            rb.constraints   = RigidbodyConstraints2D.FreezeRotation;
        }
        originalGravityScale = rb.gravityScale;
        // Interpolation smooths the rendered position between the 50 Hz physics
        // steps so movement never looks choppy at 60+ fps.
        rb.interpolation     = RigidbodyInterpolation2D.Interpolate;

        // BoxCollider2D – needed for bounds-based ground-check origin.
        col = GetComponent<BoxCollider2D>();
        if (col == null)
        {
            Debug.LogWarning("[PlayerController] BoxCollider2D was missing on '" +
                             gameObject.name + "' – adding one now.");
            col = gameObject.AddComponent<BoxCollider2D>();
        }

        SetupAimVisuals();
        SetupDashParticles();
        SetupDashEndBurst();
        SetupFireExplosionParticles();
        SetupHitFlashParticles();

        // Initialise health and dash charges.
        currentHealth      = maxHealth;
        currentDashCharges = maxDashCharges;
        spawnPosition      = transform.position;

        // Default to the neutral (no element picked up yet) dash so the player
        // always has a short, usable dash even before finding an element pickup.
        if (string.IsNullOrEmpty(currentElement.elementName))
            currentElement = CreateNeutralElement();

#if ENABLE_INPUT_SYSTEM
        InitInputActions();
#endif
    }

    private void Start()
    {
        uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
        if (uiManager == null)
            Debug.LogWarning("[PlayerController] UIManager not found – HUD updates will be skipped.");

        NotifyUIElement();
        NotifyUIDashCharges();
        NotifyUIHealth();
    }

    private void Update()
    {
        try
        {
            // FOR DEBUGGING PURPOSES ONLY — checked unconditionally (even
            // mid-dash) so R always works as an escape hatch back to spawn,
            // regardless of what state the player is currently stuck in.
            if (GetRespawnKeyDown())
                Respawn();

            if (!isDashing)
            {
                SampleInput();
                UpdateTimers();
                TryConsumeJump();
                HandleDashInput();
            }
            else
            {
                // Still tick invincibility frames while in a dash.
                TickInvincibility();
            }

            UpdateAimVisuals();
        }
        catch (Exception e)
        {
            Debug.LogError($"[PlayerController] Exception in Update: {e.Message}\n{e.StackTrace}");
        }
    }

    private void FixedUpdate()
    {
        if (rb == null)
        {
            Debug.LogError("[PlayerController] Rigidbody2D is null in FixedUpdate – " +
                           "physics cannot be applied. The component may have been destroyed.");
            return;
        }

        // Computed here (once per physics step, not once per Update()) so every
        // Update() call between now and the next FixedUpdate reads the exact
        // same value — see the comment on groundedCache's declaration.
        groundedCache = IsGrounded();

        if (isDashing) return;

        // ── Horizontal movement with acceleration / deceleration ──────────────
        // Celeste-style: snappy on the ground, momentum-preserving in the air.
        float targetX  = horizontalInput * moveSpeed;
        float currentX = rb.linearVelocity.x;
        bool  grounded = groundedCache;
        bool  hasInput = Mathf.Abs(targetX) > 0.01f;

        float accel = hasInput
            ? (grounded ? groundAcceleration : airAcceleration)
            : (grounded ? groundDeceleration : airDeceleration);

        // Fresh out of a dash: soften both accel and decel so the carried-over
        // speed keeps driving horizontal motion like a thrown projectile for a
        // moment, with gravity (unaffected, still full strength) pulling the
        // arc down into a parabola, rather than snapping straight back to
        // normal input-driven movement.
        if (dashMomentumTimer > 0f)
        {
            dashMomentumTimer -= Time.fixedDeltaTime;
            accel *= dashMomentumDecelMultiplier;
        }

        float newX = Mathf.MoveTowards(currentX, targetX, accel * Time.fixedDeltaTime);

        // ── Variable fall gravity ─────────────────────────────────────────────
        // Faster fall makes landings feel tight; the floaty apex gives extra
        // time to aim a dash at the top of a jump arc.
        float vy = rb.linearVelocity.y;
        if (vy < -0.01f)
            rb.gravityScale = originalGravityScale * fallGravityMultiplier;
        else if (vy > 0.01f && vy < jumpApexThreshold)
            rb.gravityScale = originalGravityScale * jumpApexGravityMult;
        else
            rb.gravityScale = originalGravityScale;

        rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
    }

    // ── Input ────────────────────────────────────────────────────────────────

    private void SampleInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (moveAction == null)
        {
            Debug.LogError("[PlayerController] moveAction is null. " +
                           "InitInputActions may have failed – check Input System package.");
            return;
        }
        horizontalInput = moveAction.ReadValue<float>();

        if (jumpAction != null && jumpAction.WasPressedThisFrame())
            jumpBufferCounter = jumpBufferTime;
        // Jump cut: releasing the button early reduces upward velocity so the
        // player controls jump height (Celeste-style variable jump).
        if (jumpAction != null && jumpAction.WasReleasedThisFrame()
            && rb != null && rb.linearVelocity.y > 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x,
                                            rb.linearVelocity.y * jumpCutMultiplier);
#else
        horizontalInput = 0f;
        if (Input.GetKey(KeyCode.A)         || Input.GetKey(KeyCode.LeftArrow))  horizontalInput -= 1f;
        if (Input.GetKey(KeyCode.D)         || Input.GetKey(KeyCode.RightArrow)) horizontalInput += 1f;
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow))
            jumpBufferCounter = jumpBufferTime;
        if ((Input.GetKeyUp(KeyCode.Space) || Input.GetKeyUp(KeyCode.UpArrow))
            && rb != null && rb.linearVelocity.y > 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x,
                                            rb.linearVelocity.y * jumpCutMultiplier);
#endif
    }

    // ── Dash input ────────────────────────────────────────────────────────────
    // Press → dash always fires to the element's full dashMaxDistance. No
    // hold-to-charge, and releasing the button no longer cancels an in-flight
    // dash — it always runs to completion (or stops early only on collision).

    private void HandleDashInput()
    {
        if (GetMouseButtonDown() && !isDashing && currentDashCharges > 0 && dashCooldownTimer <= 0f)
        {
            dashDirection = GetWorldMouseDirection();
            currentDashCharges--;
            NotifyUIDashCharges();
            // Dashing while airborne counts as using your jump: you can dash
            // after jumping, but not jump again after an air-dash until you
            // land. hasJumpedThisAirtime only clears on the next landing edge
            // (see UpdateTimers), so setting it here reliably blocks a repeat
            // jump for the rest of this airtime, same as TryConsumeJump does
            // for an actual jump. Zeroing the counters too is just belt-and-
            // suspenders for immediate responsiveness.
            if (!groundedCache)
            {
                hasJumpedThisAirtime = true;
                coyoteCounter        = 0f;
                jumpBufferCounter    = 0f;
            }
            StartCoroutine(PerformDash(dashDirection, currentElement.dashMaxDistance));
        }
    }

    // ── Dash coroutine ────────────────────────────────────────────────────────
    // MovePosition drives movement so we can intercept collisions before
    // the physics engine settles.  CircleCastAll is used for ahead-of-time
    // collision detection so we decide per-element what to do with each hit.
    //
    // Hybrid dash: picking up a different element mid-flight (currentElement
    // changes under us — ElementPickup calls SetElement directly) does NOT
    // reset the dash. Distance was already fixed at launch, and speed stays
    // locked to whatever element the dash started with (baseDashSpeed) — only
    // the combat/visual side (colour, particles, damage, knockback, phase-
    // through) switches over to the new element immediately. The next dash,
    // a fresh press, uses the new element fully, speed included.

    private IEnumerator PerformDash(Vector2 direction, float distance)
    {
        isDashing        = true;
        dashStartPos     = transform.position;
        rb.gravityScale  = 0f;          // disable gravity; restored on dash end
        rb.linearVelocity = Vector2.zero;

        float baseDashSpeed      = currentElement.dashSpeed;
        string activeElementName = currentElement.elementName;

        if (dashParticles != null)
        {
            var main = dashParticles.main;
            main.startColor = currentElement.elementColor;
            dashParticles.Play();
        }

        float travelledDistance = 0f;
        // Momentum feel: speed eases from dashStartSpeedMultiplier down to
        // dashEndSpeedMultiplier over the course of the dash (fast launch,
        // visible slow-down as it travels), tracked so the tail end can be
        // carried over as momentum once the dash finishes.
        float currentSpeed  = baseDashSpeed * dashStartSpeedMultiplier;
        // True only if the dash was cut short by hitting something solid — as
        // opposed to reaching its full distance naturally. Distinguishes the
        // "stop and bounce back" exit from the "carry forward into a parabola"
        // exit at the end of this coroutine.
        bool  hitObstacle    = false;
        // Enemies already processed this dash. Needed for Water (and Air, which
        // doesn't damage anyway) — without it, passing through an enemy over
        // several physics steps while overlapping it would re-trigger its
        // damage/splash/particles on every single one of those steps.
        var   alreadyHitEnemies = new HashSet<EnemyPatrol>();

        while (travelledDistance < distance)
        {
            // Element changed mid-dash (picked up a pickup while dashing): swap
            // the particle colour over now. baseDashSpeed deliberately isn't
            // touched, so the speed profile keeps following the ORIGINAL element.
            if (currentElement.elementName != activeElementName)
            {
                activeElementName = currentElement.elementName;
                if (dashParticles != null)
                {
                    var main = dashParticles.main;
                    main.startColor = currentElement.elementColor;
                }
            }

            float progress = Mathf.Clamp01(travelledDistance / distance);
            currentSpeed = Mathf.Lerp(dashStartSpeedMultiplier, dashEndSpeedMultiplier, progress)
                         * baseDashSpeed;
            float step     = currentSpeed * Time.fixedDeltaTime;
            float moveStep = Mathf.Min(step, distance - travelledDistance);

            // Scan the path ahead for enemies and solid objects. Combat rules
            // (phase-through, damage, knockback) read currentElement live, so
            // a mid-dash element switch applies immediately here too.
            var hits = Physics2D.CircleCastAll(rb.position, 0.25f, direction, moveStep + 0.05f);
            bool shouldStop = false;

            foreach (var hit in hits)
            {
                if (hit.collider == null)                          continue;
                if (hit.collider.gameObject == gameObject)         continue;

                // Enemy hit — checked BEFORE the isTrigger filter below, since
                // EnemyPatrol's own collider is a trigger (so contact damage to
                // the player works via OnTriggerEnter2D without blocking
                // movement). Filtering all triggers before this point used to
                // mean the dash could never detect an enemy at all.
                var ep = hit.collider.GetComponent<EnemyPatrol>();
                if (ep != null)
                {
                    if (!alreadyHitEnemies.Contains(ep))
                    {
                        alreadyHitEnemies.Add(ep);
                        // Guarded: an uncaught exception here would abort this
                        // coroutine mid-dash, leaving isDashing stuck true and
                        // gravity off forever (dash never restores state) —
                        // which would look exactly like "everything stopped
                        // working" after the first enemy contact.
                        try
                        {
                            if (HandleEnemyContact(ep, direction, hit.point, alreadyHitEnemies))
                            {
                                shouldStop  = true;
                                hitObstacle = true;
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[PlayerController] HandleEnemyContact threw: {e.Message}\n{e.StackTrace}");
                        }
                    }
                    continue; // never treat an enemy as a generic solid-wall block
                }

                if (hit.collider.isTrigger) continue; // pickups, wind zones

                // Solid wall / platform — always stop.
                shouldStop  = true;
                hitObstacle = true;
                break;
            }

            if (shouldStop) break;

            rb.MovePosition(rb.position + direction * moveStep);
            travelledDistance += moveStep;
            yield return new WaitForFixedUpdate();
        }

        // Safety net: the per-step CircleCastAll's lookahead shrinks to almost
        // nothing on the final step as travelledDistance closes in on distance
        // (moveStep can be tiny there), so a wall sitting right at the exact
        // edge of the dash's range can end up never quite caught by any single
        // step and the loop exits "naturally" instead of stopping on it. Catch
        // that here: if nothing already stopped the dash, do one last check
        // right at the end point before handing off to the momentum carry-over
        // — otherwise that (now quite strong) momentum just plows the player
        // into the wall via ordinary physics instead of the deliberate bounce.
        if (!hitObstacle)
        {
            var endWallHits = Physics2D.CircleCastAll(rb.position, 0.25f, direction, 0.15f);
            foreach (var endWallHit in endWallHits)
            {
                if (endWallHit.collider == null)                  continue;
                if (endWallHit.collider.gameObject == gameObject) continue;
                if (endWallHit.collider.isTrigger)                continue; // enemies, pickups, wind zone
                hitObstacle = true;
                break;
            }
        }

        // Water/Air: if the dash's natural end point still overlaps an enemy
        // (it phased through and simply ran out of distance on top of it),
        // shove that enemy away hard — a distinct, much stronger push than the
        // incidental contact knockback dealt while passing through it.
        if (!hitObstacle &&
            (currentElement.elementName == "Water" || currentElement.elementName == "Air"))
        {
            var endHits = Physics2D.OverlapCircleAll(rb.position, 0.4f);
            foreach (var endHit in endHits)
            {
                var endEp = endHit.GetComponent<EnemyPatrol>();
                if (endEp == null) continue;

                Vector2 away = (Vector2)endEp.transform.position - rb.position;
                Vector2 push = (away.magnitude > 0.01f ? away.normalized : direction)
                             * dashEndEnemyPushStrength;
                endEp.TakeDamage(0f, push);
            }
        }

        // ── Restore state ─────────────────────────────────────────────────────
        rb.gravityScale   = originalGravityScale;
        if (hitObstacle)
        {
            // Stopped early by a wall/enemy: kill forward motion and recoil
            // slightly the other way instead of continuing to push into
            // whatever was just hit.
            rb.linearVelocity = -direction * dashBounceStrength;
            dashMomentumTimer = dashBounceHoldTime;
        }
        else
        {
            // Reached full distance: exit still moving fast in the dash
            // direction (almost the full ending speed, see
            // dashMomentumCarryover) instead of stopping. Gravity is back to
            // full strength as of the line above, so from here the player
            // arcs downward like a thrown projectile — a parabola — while
            // dashMomentumTimer keeps FixedUpdate's horizontal control soft
            // for a moment so that arc isn't immediately flattened back to
            // normal movement speed.
            rb.linearVelocity = direction * (currentSpeed * dashMomentumCarryover);
            dashMomentumTimer = dashMomentumHoldTime;
        }
        isDashing         = false;
        dashCooldownTimer = dashCooldown;
        // StopEmitting (not Stop): already-spawned trail particles keep fading
        // out naturally instead of being cut off mid-life.
        dashParticles?.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        // Invalidate wasGrounded so the very next UpdateTimers call sees a fresh
        // landing event even if the dash started and ended on the ground.  Without
        // this the grounded&&!wasGrounded condition never fires after a ground dash.
        wasGrounded       = false;

        Camera.main?.GetComponent<CameraController>()?.TriggerCatchUp();

        // Burst at the exact point the dash ends — full distance, obstacle
        // bounce, or otherwise. Coloured by the live currentElement, so a
        // hybrid dash's burst matches whatever element it ended as. Kept as
        // the very last thing this coroutine does, wrapped defensively, so it
        // can never affect the stop/bounce state above even if something about
        // the particle system throws.
        try
        {
            EmitDashEndBurst(rb.position, currentElement.elementColor);
        }
        catch (Exception e)
        {
            Debug.LogError($"[PlayerController] EmitDashEndBurst threw: {e.Message}");
        }
    }

    // ── Elemental combat ─────────────────────────────────────────────────────
    // Each element's rules for what happens when a dash touches an enemy.
    // Returns true if the dash should stop here (Earth, Fire), false if it
    // should keep travelling (Water, Air).
    private bool HandleEnemyContact(EnemyPatrol ep, Vector2 direction, Vector2 hitPoint,
                                     HashSet<EnemyPatrol> alreadyHitEnemies)
    {
        switch (currentElement.elementName)
        {
            case "Earth":
                // Single target, one-shot kill, then stop — no AoE, no pass-through.
                ep.TakeDamage(currentElement.damage, direction * currentElement.enemyKnockback);
                Debug.Log($"[PlayerController] Earth dash hit '{ep.gameObject.name}' and stopped.");
                return true;

            case "Fire":
            {
                // AoE explosion at the impact point: every enemy caught in
                // fireExplosionRadius takes Fire's one-shot damage, not just
                // the one directly touched.
                var splash = Physics2D.OverlapCircleAll(hitPoint, fireExplosionRadius);
                foreach (var s in splash)
                {
                    var sep = s.GetComponent<EnemyPatrol>();
                    if (sep == null) continue;
                    Vector2 away = (Vector2)sep.transform.position - hitPoint;
                    Vector2 kb   = (away.magnitude > 0.01f ? away.normalized : direction)
                                 * currentElement.enemyKnockback;
                    sep.TakeDamage(currentElement.damage, kb);
                }
                EmitFireExplosion(hitPoint);
                Debug.Log($"[PlayerController] Fire dash exploded at {hitPoint} " +
                          $"(radius {fireExplosionRadius}) and stopped.");
                return true;
            }

            case "Water":
            {
                // Small splash around the contact point: every enemy caught in
                // it (including the one directly touched) takes half of its
                // OWN max health — exactly two hits to kill regardless of a
                // given enemy's configured health. Dash keeps going afterward.
                var splash = Physics2D.OverlapCircleAll(hitPoint, waterSplashRadius);
                foreach (var s in splash)
                {
                    var sep = s.GetComponent<EnemyPatrol>();
                    if (sep == null) continue;
                    // The directly-hit enemy (ep) was already added to
                    // alreadyHitEnemies by the caller BEFORE this method ran,
                    // purely as a guard against HandleEnemyContact being
                    // invoked again for it on a later physics step — not as a
                    // sign it's already been damaged. Treating that the same
                    // as "already splashed" here meant the primary target
                    // itself was silently skipped and took no damage. Only
                    // skip enemies OTHER than ep that a previous splash this
                    // dash already hit.
                    if (sep != ep && alreadyHitEnemies.Contains(sep)) continue;
                    alreadyHitEnemies.Add(sep);
                    sep.TakeDamage(sep.MaxHealth * 0.5f, direction * currentElement.enemyKnockback);
                }
                EmitDashEndBurst(hitPoint, currentElement.elementColor);
                return false;
            }

            default:
                // Air: pure pass-through, no damage at all. (Also covers the
                // neutral pre-pickup element, which has no combat identity yet.)
                return false;
        }
    }

    // ── Timers ────────────────────────────────────────────────────────────────

    private void UpdateTimers()
    {
        // groundedCache, not a fresh IsGrounded() call: see its declaration for
        // why reading a physics-synced snapshot here (rather than querying
        // spatial state from Update()-driven code) is what actually makes the
        // coyote-time/jump-buffer combo below race-free.
        bool grounded   = groundedCache;
        bool justLanded = grounded && !wasGrounded;

        // Landing resets dash charges (not while still inside a dash arc).
        if (justLanded && !isDashing)
        {
            currentDashCharges = maxDashCharges;
            NotifyUIDashCharges();
        }

        // Re-arm jumping only on the landing EDGE, not on every continuously-
        // grounded frame — see hasJumpedThisAirtime's declaration for why that
        // distinction is what actually stops a mashed double-jump.
        if (justLanded)
            hasJumpedThisAirtime = false;

        wasGrounded = grounded;

        // Canonical coyote-time pattern: full while grounded, ticking down the
        // moment you're not.
        if (grounded)
            coyoteCounter = coyoteTime;
        else
            coyoteCounter -= Time.deltaTime;

        jumpBufferCounter -= Time.deltaTime;

        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;

        TickInvincibility();
    }

    private void TickInvincibility()
    {
        if (!isInvincible) return;
        invincibilityTimer -= Time.deltaTime;
        if (invincibilityTimer <= 0f)
            isInvincible = false;
    }

    private void TryConsumeJump()
    {
        if (jumpBufferCounter > 0f && coyoteCounter > 0f && !hasJumpedThisAirtime)
        {
            Jump();
            hasJumpedThisAirtime = true;
            jumpBufferCounter    = 0f;
            coyoteCounter        = 0f;
        }
    }

    private void Jump()
    {
        if (rb == null)
        {
            Debug.LogError("[PlayerController] Cannot jump: Rigidbody2D is null.");
            return;
        }
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
    }

    // ── Ground check ─────────────────────────────────────────────────────────

    private bool IsGrounded()
    {
        if (col == null) return false;

        // BoxCast covers 90 % of the collider width so landing on a tile edge
        // still registers as grounded.  Origin starts just outside the bottom
        // face to avoid self-detection (player and tilemap share the Default layer).
        Vector2 castSize = new Vector2(col.bounds.size.x * 0.9f, 0.05f);
        Vector2 origin   = (Vector2)transform.position +
                           Vector2.down * (col.bounds.extents.y + 0.01f);
        return Physics2D.BoxCast(origin, castSize, 0f, Vector2.down,
                                 groundCheckDistance, groundLayer).collider != null;
    }

    // ── Aim visuals ──────────────────────────────────────────────────────────
    // A border-only circle sized to the current element's max dash distance,
    // plus a cursor dot that follows the mouse but is locked to the inside of
    // that circle. Both only appear when a dash charge is actually available,
    // and both disappear the instant a dash starts.

    private void SetupAimVisuals()
    {
        var circleGO = new GameObject("AimRangeCircle");
        circleGO.transform.SetParent(transform, false);
        aimRangeCircle = circleGO.AddComponent<LineRenderer>();
        aimRangeCircle.loop          = true;
        aimRangeCircle.useWorldSpace = true;
        aimRangeCircle.positionCount = Mathf.Max(3, aimCircleSegments);
        aimRangeCircle.startWidth    = aimRangeCircle.endWidth = aimCircleWidth;
        aimRangeCircle.material      = new Material(Shader.Find("Sprites/Default"));
        aimRangeCircle.enabled       = false;

        var cursorGO = new GameObject("AimCursor");
        cursorGO.transform.SetParent(transform, false);
        aimCursor = cursorGO.AddComponent<SpriteRenderer>();
        aimCursor.sprite       = CreateCircleSprite(Color.white);
        aimCursor.sortingOrder = 10;
        cursorGO.transform.localScale = Vector3.one * aimCursorSize;
        aimCursor.enabled = false;
    }

    private void UpdateAimVisuals()
    {
        if (aimRangeCircle == null || aimCursor == null) return;

        bool dashAvailable = !isDashing && currentDashCharges > 0 && dashCooldownTimer <= 0f;
        if (!dashAvailable)
        {
            aimRangeCircle.enabled = false;
            aimCursor.enabled      = false;
            return;
        }

        float   radius = currentElement.dashMaxDistance;
        Color   c      = currentElement.elementColor;
        Vector2 center = transform.position;

        aimRangeCircle.enabled    = true;
        aimRangeCircle.startColor = aimRangeCircle.endColor = c;
        int segments = aimRangeCircle.positionCount;
        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)segments * Mathf.PI * 2f;
            aimRangeCircle.SetPosition(i, center + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radius);
        }

        // Raw (unnormalised) offset to the mouse — clamp its length to the
        // circle radius so the cursor locks to the edge when aiming outside it.
        Vector2 rawOffset = GetWorldMousePosition() - center;
        Vector2 clamped   = rawOffset.magnitude > radius ? rawOffset.normalized * radius : rawOffset;

        aimCursor.enabled       = true;
        aimCursor.color         = c;
        aimCursor.transform.position = center + clamped;
    }

    /// <summary>Small solid-colour circle sprite, tinted per-instance via SpriteRenderer.color.</summary>
    private static Sprite CreateCircleSprite(Color color)
    {
        const int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                tex.SetPixel(x, y, dx * dx + dy * dy <= r * r ? color : Color.clear);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // ── Dash particles ───────────────────────────────────────────────────────
    // One ParticleSystem, recoloured per-dash to currentElement.elementColor.
    // Parented to the player so MovePosition-driven dash steps leave a trail.

    private void SetupDashParticles()
    {
        var psGO = new GameObject("DashParticles");
        psGO.transform.SetParent(transform, false);
        dashParticles = psGO.AddComponent<ParticleSystem>();

        var main = dashParticles.main;
        main.loop               = false;
        main.startLifetime      = 0.4f;
        main.startSpeed         = 1f;
        main.startSize          = 0.15f;
        main.simulationSpace    = ParticleSystemSimulationSpace.World;
        main.stopAction          = ParticleSystemStopAction.None;

        var emission = dashParticles.emission;
        emission.rateOverTime = dashParticleRate;

        var shape = dashParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.15f;

        var renderer = psGO.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
            renderer.material = new Material(Shader.Find("Sprites/Default"));

        dashParticles.Stop();
    }

    // ── Dash end burst ───────────────────────────────────────────────────────
    // A separate, one-shot ParticleSystem (no continuous emission of its own —
    // triggered manually via Emit()) so the trailing dash particles and the
    // end-of-dash burst can have completely different shapes/lifetimes without
    // fighting over one system's settings.

    private void SetupDashEndBurst()
    {
        var psGO = new GameObject("DashEndBurst");
        psGO.transform.SetParent(transform, false);
        dashEndBurst = psGO.AddComponent<ParticleSystem>();

        var main = dashEndBurst.main;
        main.loop          = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        // Randomised speed (not a fixed value) is what breaks up the "clean
        // expanding ring" look — every particle travels a different distance,
        // so the burst reads as a scatter instead of a uniform circle outline.
        main.startSpeed      = new ParticleSystem.MinMaxCurve(2.5f, 7f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction      = ParticleSystemStopAction.None;

        // No rateOverTime: particles are only ever added via Emit() calls.
        var emission = dashEndBurst.emission;
        emission.rateOverTime = 0f;

        // Emitting from a point with full-circle random direction (rather than
        // the dash trail's narrow forward cone) is what makes this read as an
        // "explosion" instead of more trail. randomDirectionAmount = 1 is the
        // other half of that: without it, particles still launch in a neat
        // radial pattern outward from the shape (a clean circle); with it,
        // each particle's direction is fully randomised instead.
        var shape = dashEndBurst.shape;
        shape.shapeType            = ParticleSystemShapeType.Circle;
        shape.radius               = 0.05f;
        shape.arc                  = 360f;
        shape.randomDirectionAmount = 1f;

        var renderer = psGO.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            // Render behind the player sprite (default sortingOrder 0) so a
            // burst at the point of impact never visually covers the character
            // right when it's most important to see it actually stop.
            renderer.sortingOrder = -1;
        }

        dashEndBurst.Stop();
    }

    /// <summary>Fires the one-shot particle burst at the given world position, coloured per-call.</summary>
    private void EmitDashEndBurst(Vector2 worldPosition, Color color)
    {
        if (dashEndBurst == null) return;

        dashEndBurst.transform.position = worldPosition;
        var main = dashEndBurst.main;
        main.startColor = color;
        dashEndBurst.Emit(dashEndBurstCount);
    }

    // ── Fire explosion particles ─────────────────────────────────────────────
    // A separate, bigger one-shot burst from dashEndBurst: particles spawn
    // spread across a circle sized to fireExplosionRadius (not from a single
    // point), so the visual footprint actually reads as "this whole area just
    // got hit" rather than a small point-burst.

    private void SetupFireExplosionParticles()
    {
        var psGO = new GameObject("FireExplosionParticles");
        psGO.transform.SetParent(transform, false);
        fireExplosionParticles = psGO.AddComponent<ParticleSystem>();

        var main = fireExplosionParticles.main;
        main.loop          = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        // Randomised, and pushed further out than before — this plus
        // randomDirectionAmount below is what turns "particles spawned across
        // a disk, launched in a clean radial pattern" (reads as a circle) into
        // an actual chaotic scatter with real reach past fireExplosionRadius.
        main.startSpeed      = new ParticleSystem.MinMaxCurve(2f, 9f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction      = ParticleSystemStopAction.None;

        var emission = fireExplosionParticles.emission;
        emission.rateOverTime = 0f;

        var shape = fireExplosionParticles.shape;
        shape.shapeType             = ParticleSystemShapeType.Circle;
        shape.radius                = fireExplosionRadius;
        shape.arc                   = 360f;
        shape.randomDirectionAmount = 1f; // scatter, not a clean expanding ring

        var renderer = psGO.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.material     = new Material(Shader.Find("Sprites/Default"));
            renderer.sortingOrder = -1;
        }

        fireExplosionParticles.Stop();
    }

    private void EmitFireExplosion(Vector2 worldPosition)
    {
        if (fireExplosionParticles == null) return;

        fireExplosionParticles.transform.position = worldPosition;
        var main = fireExplosionParticles.main;
        main.startColor = currentElement.elementColor;
        fireExplosionParticles.Emit(72); // denser burst to fill the wider spread
    }

    // ── Hit flash particles ──────────────────────────────────────────────────
    // A red one-shot burst emitted on the player's own position whenever
    // TakeDamage actually applies damage (not when it's ignored due to
    // invincibility or dash i-frames) — quick, chaotic hit feedback.

    private void SetupHitFlashParticles()
    {
        var psGO = new GameObject("HitFlashParticles");
        psGO.transform.SetParent(transform, false);
        hitFlashParticles = psGO.AddComponent<ParticleSystem>();

        var main = hitFlashParticles.main;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(3f, 7f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
        main.startColor      = new Color(1f, 0.12f, 0.08f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction      = ParticleSystemStopAction.None;

        var emission = hitFlashParticles.emission;
        emission.rateOverTime = 0f;

        var shape = hitFlashParticles.shape;
        shape.shapeType             = ParticleSystemShapeType.Circle;
        shape.radius                = 0.3f;
        shape.arc                   = 360f;
        shape.randomDirectionAmount = 1f; // chaotic scatter, matching the dash-end bursts

        var renderer = psGO.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
            renderer.material = new Material(Shader.Find("Sprites/Default"));

        hitFlashParticles.Stop();
    }

    private void EmitHitFlash()
    {
        if (hitFlashParticles == null) return;

        hitFlashParticles.transform.position = transform.position;
        hitFlashParticles.Emit(28);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies damage to the player.  Invincibility frames prevent rapid multi-hits.
    /// On death health resets and the player is teleported back to their spawn.
    /// </summary>
    public void TakeDamage(int damage)
    {
        if (isInvincible)
        {
            Debug.Log("[PlayerController] TakeDamage ignored – player is currently invincible.");
            return;
        }
        if (damage <= 0)
        {
            Debug.LogWarning($"[PlayerController] TakeDamage called with damage={damage}. " +
                             "Value must be positive; ignoring.");
            return;
        }

        currentHealth -= damage;
        Debug.Log($"[PlayerController] Took {damage} damage. Health: {currentHealth}/{maxHealth}.");
        EmitHitFlash();
        NotifyUIHealth();

        if (currentHealth <= 0)
        {
            Debug.Log("[PlayerController] Health reached 0 – showing Game Over screen.");
            if (uiManager != null)
            {
                try   { uiManager.ShowGameOver(); }
                catch (Exception e) { Debug.LogError($"[PlayerController] UIManager.ShowGameOver threw: {e.Message}"); }
            }
            else
            {
                Debug.LogWarning("[PlayerController] UIManager not found – auto-respawning " +
                                 "instead of showing a Game Over screen.");
                Respawn();
            }
        }
        else
        {
            isInvincible       = true;
            invincibilityTimer = invincibilityDuration;
        }
    }

    /// <summary>
    /// Bound to the R key (see SampleInput) as a DEBUGGING-purposes reset, and
    /// also the target of the Game Over screen's Respawn button, and used
    /// internally as a fallback if that screen can't be shown. Always hides
    /// the Game Over screen and resumes time itself (rather than relying on
    /// each caller to do it), so the result is consistent no matter which of
    /// those triggered it.
    /// </summary>
    public void Respawn()
    {
        currentHealth       = maxHealth;
        transform.position  = spawnPosition;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale   = originalGravityScale; // in case this hit mid-dash (gravity was zeroed)
        }
        isDashing             = false;
        dashMomentumTimer     = 0f;
        dashCooldownTimer     = 0f;
        currentDashCharges    = maxDashCharges;
        isInvincible          = false;
        invincibilityTimer    = 0f;
        NotifyUIDashCharges();
        NotifyUIHealth();

        if (uiManager != null)
        {
            try   { uiManager.HideGameOver(); }
            catch (Exception e) { Debug.LogError($"[PlayerController] UIManager.HideGameOver threw: {e.Message}"); }
        }

        Debug.Log("[PlayerController] Respawned at spawn position (DEBUG reset / death).");
    }

    /// <summary>
    /// Switches to a new element and updates the HUD.
    /// Safe to call from ElementPickup or any progression system.
    /// </summary>
    public void SetElement(ElementStats newElement)
    {
        if (string.IsNullOrEmpty(newElement.elementName))
        {
            Debug.LogError("[PlayerController] SetElement: supplied ElementStats has no name. " +
                           "Element will not be changed.");
            return;
        }
        currentElement = newElement;
        Debug.Log($"[PlayerController] Element switched to '{currentElement.elementName}'.");
        NotifyUIElement();
    }

    /// <summary>
    /// Permanently increases max air-dash charges by 1.
    /// Called from upgrade / progression systems.
    /// </summary>
    public void UnlockExtraDash()
    {
        maxDashCharges++;
        currentDashCharges = Mathf.Min(currentDashCharges + 1, maxDashCharges);
        Debug.Log($"[PlayerController] Extra dash unlocked – maxDashCharges = {maxDashCharges}.");
        NotifyUIDashCharges();
    }

    /// <summary>
    /// Read by EnemyProjectile to determine if Water's phase-through mechanic applies.
    /// </summary>
    public bool IsDashingWithPhaseProjectiles =>
        isDashing && currentElement.phaseThroughProjectiles;

    /// <summary>Called by CameraController to check whether a dash is in progress.</summary>
    public bool IsDashing() => isDashing;

    /// <summary>
    /// True while dashing OR still coasting on the post-dash momentum carry-over
    /// (dashMomentumTimer). During that whole window, horizontal deceleration is
    /// zero or heavily reduced, so any external force applied here (e.g. wind)
    /// has nothing opposing it and just silently accumulates — read by
    /// WindZoneController to suppress wind for the same span.
    /// </summary>
    public bool IsDashingOrCoasting() => isDashing || dashMomentumTimer > 0f;

    /// <summary>Called by CameraController to get the current dash direction for lag-offset.</summary>
    public Vector2 GetDashDirection() => dashDirection;

    // ── UI notification helpers ───────────────────────────────────────────────

    private void NotifyUIElement()
    {
        if (uiManager == null) return;
        try   { uiManager.UpdateElementDisplay(currentElement); }
        catch (Exception e) { Debug.LogError($"[PlayerController] UIManager.UpdateElementDisplay threw: {e.Message}"); }
    }

    private void NotifyUIDashCharges()
    {
        if (uiManager == null) return;
        try   { uiManager.UpdateDashCharges(currentDashCharges, maxDashCharges); }
        catch (Exception e) { Debug.LogError($"[PlayerController] UIManager.UpdateDashCharges threw: {e.Message}"); }
    }

    private void NotifyUIHealth()
    {
        if (uiManager == null) return;
        try   { uiManager.UpdateHealthDisplay(currentHealth, maxHealth); }
        catch (Exception e) { Debug.LogError($"[PlayerController] UIManager.UpdateHealthDisplay threw: {e.Message}"); }
    }

    // ── Mouse helpers (dual Input System path) ────────────────────────────────

    private bool GetMouseButtonDown()
    {
#if ENABLE_INPUT_SYSTEM
        return dashAction != null && dashAction.WasPressedThisFrame();
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private bool GetMouseButton()
    {
#if ENABLE_INPUT_SYSTEM
        return dashAction != null && dashAction.IsPressed();
#else
        return Input.GetMouseButton(0);
#endif
    }

    private bool GetMouseButtonUp()
    {
#if ENABLE_INPUT_SYSTEM
        return dashAction != null && dashAction.WasReleasedThisFrame();
#else
        return Input.GetMouseButtonUp(0);
#endif
    }

    /// <summary>FOR DEBUGGING PURPOSES ONLY — the R key triggers Respawn().</summary>
    private bool GetRespawnKeyDown()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }

    private Vector2 GetWorldMousePosition()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[PlayerController] Camera.main is null \u2013 mouse position defaults to the " +
                           "player's position. Ensure the Main Camera has the 'MainCamera' tag.");
            return transform.position;
        }
#if ENABLE_INPUT_SYSTEM
        Vector2 screenPos = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : (Vector2)Input.mousePosition;
#else
        Vector2 screenPos = Input.mousePosition;
#endif
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
        world.z = 0f;
        return world;
    }

    private Vector2 GetWorldMouseDirection()
    {
        Vector2 dir = GetWorldMousePosition() - (Vector2)transform.position;
        return dir.magnitude > 0.01f ? dir.normalized : Vector2.right;
    }

    // ── New Input System setup / teardown ────────────────────────────────────

#if ENABLE_INPUT_SYSTEM
    private void InitInputActions()
    {
        try
        {
            moveAction = new InputAction("Move",
                InputActionType.Value,
                expectedControlType: "Axis");

            moveAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");

            moveAction.AddBinding("<Gamepad>/leftStick/x");
            moveAction.Enable();

            jumpAction = new InputAction("Jump", InputActionType.Button);
            jumpAction.AddBinding("<Keyboard>/space");
            jumpAction.AddBinding("<Keyboard>/upArrow");
            jumpAction.AddBinding("<Gamepad>/buttonSouth");
            jumpAction.Enable();

            dashAction = new InputAction("Dash", InputActionType.Button);
            dashAction.AddBinding("<Mouse>/leftButton");
            dashAction.AddBinding("<Gamepad>/rightTrigger");
            dashAction.Enable();
        }
        catch (Exception e)
        {
            Debug.LogError($"[PlayerController] Failed to initialise InputActions: {e.Message}\n" +
                           "Verify the Input System package (com.unity.inputsystem) is installed " +
                           "and active in Project Settings > Player > Active Input Handling.");
        }
    }

    private void OnDisable()
    {
        moveAction?.Disable();
        jumpAction?.Disable();
        dashAction?.Disable();
    }

    private void OnDestroy()
    {
        moveAction?.Dispose();
        jumpAction?.Dispose();
        dashAction?.Dispose();
    }
#endif
}
