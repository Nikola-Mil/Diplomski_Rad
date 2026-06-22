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
    public float moveSpeed          = 8f;
    public float jumpForce          = 12f;

    [Header("Ground Detection")]
    [Tooltip("Layer(s) counted as ground for IsGrounded raycasts.")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.1f;

    [Header("Feel")]
    [Tooltip("Seconds the player can still jump after leaving a ledge.")]
    public float coyoteTime          = 0.1f;
    [Tooltip("Seconds a jump press is remembered before landing.")]
    public float jumpBufferTime      = 0.1f;

    // ── Dash ──────────────────────────────────────────────────────────────────
    [Header("Dash – Charges")]
    [Tooltip("Minimum hold time in seconds before a dash will fire on release.")]
    public float dashMinChargeTime = 0.1f;
    [Tooltip("Hold for this many seconds to reach maximum dash distance.")]
    public float dashMaxChargeTime = 1.5f;
    [Tooltip("Seconds after a dash ends before the next one can start.")]
    public float dashCooldown      = 0.5f;
    [Tooltip("Number of air-dashes available before landing replenishes them.")]
    public int   maxDashCharges    = 1;
    [Tooltip("World-unit length shown on the trajectory preview at full charge.")]
    public float dashTrajectoryPreviewLength = 3f;

    // ── Element ───────────────────────────────────────────────────────────────
    [Header("Element")]
    [Tooltip("Active element – governs dash behaviour. Changed via SetElement().")]
    public ElementStats currentElement;

    // ── Health ────────────────────────────────────────────────────────────────
    [Header("Health")]
    [Tooltip("Starting health points.")]
    public int   maxHealth            = 3;
    [Tooltip("Invincibility window after taking damage (seconds).")]
    public float invincibilityDuration = 1.5f;

    // ── Private movement state ────────────────────────────────────────────────────
    private Rigidbody2D   rb;
    private BoxCollider2D col;
    private float         horizontalInput;
    private float         coyoteCounter;
    private float         jumpBufferCounter;
    private bool          wasGrounded;
    private float         originalGravityScale;

    // ── Private dash state ────────────────────────────────────────────────────────
    private int           currentDashCharges;
    private bool          isDashing;
    private bool          isChargingDash;
    private float         dashChargeTimer;
    private Vector2       dashDirection;
    private Vector2       dashStartPos;      // unused directly but useful for debug gizmos
    private float         dashCooldownTimer;
    private bool          dashInterrupted;   // set mid-coroutine to abort early
    private LineRenderer  trajectoryLine;

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
        damage                  = 25f,
        phaseThroughEnemies     = false,
        phaseThroughProjectiles = false,
        enemyKnockback          = 18f,   // high: crowd-control hammer
        elementColor            = new Color(0.3f, 0.7f, 0.2f)
    };

    public static ElementStats CreateFireElement() => new ElementStats
    {
        elementName             = "Fire",
        dashSpeed               = 10f,
        dashMaxDistance         = 6f,
        damage                  = 20f,
        phaseThroughEnemies     = false,
        phaseThroughProjectiles = false,
        enemyKnockback          = 8f,
        elementColor            = new Color(1f, 0.3f, 0.05f)
    };

    public static ElementStats CreateWaterElement() => new ElementStats
    {
        elementName             = "Water",
        dashSpeed               = 10f,
        dashMaxDistance         = 6f,
        damage                  = 0f,    // evasion-only
        phaseThroughEnemies     = false,
        phaseThroughProjectiles = true,  // key Water trait: ignore incoming fire
        enemyKnockback          = 4f,
        elementColor            = new Color(0.1f, 0.5f, 1f)
    };

    public static ElementStats CreateAirElement() => new ElementStats
    {
        elementName             = "Air",
        dashSpeed               = 16f,
        dashMaxDistance         = 10f,
        damage                  = 0f,    // traversal-only
        phaseThroughEnemies     = true,  // key Air trait: ghost through enemy bodies
        phaseThroughProjectiles = false,
        enemyKnockback          = 0f,
        elementColor            = new Color(0.9f, 0.9f, 1f)
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
            rb.gravityScale  = 3f;
            rb.constraints   = RigidbodyConstraints2D.FreezeRotation;
        }
        originalGravityScale = rb.gravityScale;

        // BoxCollider2D – needed for bounds-based ground-check origin.
        col = GetComponent<BoxCollider2D>();
        if (col == null)
        {
            Debug.LogWarning("[PlayerController] BoxCollider2D was missing on '" +
                             gameObject.name + "' – adding one now.");
            col = gameObject.AddComponent<BoxCollider2D>();
        }

        // LineRenderer for dash trajectory preview.
        trajectoryLine = GetComponent<LineRenderer>();
        if (trajectoryLine == null)
        {
            Debug.LogWarning("[PlayerController] LineRenderer not found – adding one for dash " +
                             "trajectory preview. Assign a material in the Inspector.");
            trajectoryLine = gameObject.AddComponent<LineRenderer>();
            trajectoryLine.positionCount = 2;
            trajectoryLine.startWidth    = 0.06f;
            trajectoryLine.endWidth      = 0.01f;
            trajectoryLine.useWorldSpace = true;
        }
        trajectoryLine.enabled = false;

        // Initialise health and dash charges.
        currentHealth      = maxHealth;
        currentDashCharges = maxDashCharges;
        spawnPosition      = transform.position;

        // Default to Air so the dash works immediately without any pickup.
        if (string.IsNullOrEmpty(currentElement.elementName))
            currentElement = CreateAirElement();

#if ENABLE_INPUT_SYSTEM
        InitInputActions();
#endif
    }

    private void Start()
    {
        uiManager = UnityEngine.Object.FindObjectOfType<UIManager>();
        if (uiManager == null)
            Debug.LogWarning("[PlayerController] UIManager not found – HUD updates will be skipped.");

        NotifyUIElement();
        NotifyUIDashCharges();
    }

    private void Update()
    {
        try
        {
            if (!isDashing)
            {
                SampleInput();
                UpdateTimers();
                TryConsumeJump();
                HandleDashChargeInput();
            }
            else
            {
                // Still tick invincibility frames while in a dash.
                TickInvincibility();
            }

            UpdateTrajectoryPreview();
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

        // The dash coroutine owns all movement while isDashing; skip normal velocity write.
        if (isDashing) return;

        // Preserve vertical velocity so gravity and jump are not overwritten.
        rb.linearVelocity = new Vector2(horizontalInput * moveSpeed, rb.linearVelocity.y);
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
#else
        horizontalInput = 0f;
        if (Input.GetKey(KeyCode.A)         || Input.GetKey(KeyCode.LeftArrow))  horizontalInput -= 1f;
        if (Input.GetKey(KeyCode.D)         || Input.GetKey(KeyCode.RightArrow)) horizontalInput += 1f;
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow))
            jumpBufferCounter = jumpBufferTime;
#endif
    }

    // ── Dash charge input ─────────────────────────────────────────────────────

    private void HandleDashChargeInput()
    {
        bool mouseDown = GetMouseButtonDown();
        bool mouseHeld = GetMouseButton();
        bool mouseUp   = GetMouseButtonUp();

        // Begin charging – only allowed when charges remain and cooldown is clear.
        if (mouseDown && !isChargingDash && currentDashCharges > 0 && dashCooldownTimer <= 0f)
        {
            isChargingDash  = true;
            dashChargeTimer = 0f;
        }

        // Accumulate charge time and track mouse direction every frame.
        if (isChargingDash && mouseHeld)
        {
            dashChargeTimer = Mathf.Clamp(dashChargeTimer + Time.deltaTime, 0f, dashMaxChargeTime);
            dashDirection   = GetWorldMouseDirection();

            if (uiManager != null)
                uiManager.UpdateDashChargeMeter(dashChargeTimer / dashMaxChargeTime);
        }

        // Release: fire if charge met minimum, otherwise cancel silently.
        if (isChargingDash && mouseUp)
        {
            isChargingDash = false;
            if (uiManager != null)
                uiManager.UpdateDashChargeMeter(0f);

            if (dashChargeTimer >= dashMinChargeTime)
            {
                float distance = (dashChargeTimer / dashMaxChargeTime) * currentElement.dashMaxDistance;
                distance = Mathf.Max(distance, 0.5f);
                currentDashCharges--;
                NotifyUIDashCharges();
                StartCoroutine(PerformDash(dashDirection, distance));
            }
            else
            {
                Debug.Log($"[PlayerController] Dash cancelled – charge time {dashChargeTimer:F2}s " +
                          $"is below minimum {dashMinChargeTime:F2}s.");
            }
            dashChargeTimer = 0f;
        }
    }

    // ── Dash coroutine ────────────────────────────────────────────────────────
    // MovePosition drives movement so we can intercept collisions before
    // the physics engine settles.  CircleCastAll is used for ahead-of-time
    // collision detection so we decide per-element what to do with each hit.

    private IEnumerator PerformDash(Vector2 direction, float distance)
    {
        isDashing        = true;
        dashInterrupted  = false;
        dashStartPos     = transform.position;
        rb.gravityScale  = 0f;          // disable gravity; restored on dash end
        rb.linearVelocity = Vector2.zero;

        float travelledDistance = 0f;
        float step = currentElement.dashSpeed * Time.fixedDeltaTime;

        while (travelledDistance < distance && !dashInterrupted)
        {
            float moveStep = Mathf.Min(step, distance - travelledDistance);

            // Scan the path ahead for enemies and solid objects.
            var hits = Physics2D.CircleCastAll(rb.position, 0.25f, direction, moveStep + 0.05f);
            bool shouldStop = false;

            foreach (var hit in hits)
            {
                if (hit.collider == null)                          continue;
                if (hit.collider.gameObject == gameObject)         continue;
                if (hit.collider.isTrigger)                        continue; // pickups, wind zones

                // Enemy hit ────────────────────────────────────────────────────
                var ep = hit.collider.GetComponent<EnemyPatrol>();
                if (ep != null)
                {
                    if (currentElement.phaseThroughEnemies)
                    {
                        // Air: ghost dash — do not interact with enemy bodies.
                        // This is the core Air mechanic: pure traversal with no combat value.
                        continue;
                    }
                    // Earth / Fire: deal damage + knockback, then stop.
                    ep.TakeDamage(currentElement.damage, direction * currentElement.enemyKnockback);
                    Debug.Log($"[PlayerController] {currentElement.elementName} dash hit " +
                              $"'{ep.gameObject.name}': {currentElement.damage} dmg, " +
                              $"knockback {currentElement.enemyKnockback}.");
                    shouldStop = true;
                    break;
                }

                // Solid wall / platform — always stop.
                shouldStop = true;
                break;
            }

            if (shouldStop) break;

            rb.MovePosition(rb.position + direction * moveStep);
            travelledDistance += moveStep;
            yield return new WaitForFixedUpdate();
        }

        // ── Restore state ─────────────────────────────────────────────────────
        rb.gravityScale   = originalGravityScale;
        rb.linearVelocity = Vector2.zero;
        isDashing         = false;
        dashCooldownTimer = dashCooldown;
    }

    // ── Timers ────────────────────────────────────────────────────────────────

    private void UpdateTimers()
    {
        bool grounded = IsGrounded();

        // Landing resets dash charges (not while still inside a dash arc).
        if (grounded && !wasGrounded && !isDashing)
        {
            currentDashCharges = maxDashCharges;
            NotifyUIDashCharges();
        }

        wasGrounded        = grounded;
        coyoteCounter      = grounded ? coyoteTime : coyoteCounter - Time.deltaTime;
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
        if (jumpBufferCounter > 0f && coyoteCounter > 0f)
        {
            Jump();
            jumpBufferCounter = 0f;
            coyoteCounter     = 0f;
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

        Vector2 origin = (Vector2)transform.position +
                         Vector2.down * (col.bounds.extents.y - 0.01f);
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down,
                                              groundCheckDistance, groundLayer);
        return hit.collider != null;
    }

    // ── Trajectory preview ────────────────────────────────────────────────────

    private void UpdateTrajectoryPreview()
    {
        if (trajectoryLine == null) return;

        if (isChargingDash)
        {
            trajectoryLine.enabled = true;
            float previewDist = (dashChargeTimer / dashMaxChargeTime) * dashTrajectoryPreviewLength;
            trajectoryLine.SetPosition(0, transform.position);
            trajectoryLine.SetPosition(1, (Vector2)transform.position + dashDirection * previewDist);
            Color c = currentElement.elementColor;
            trajectoryLine.startColor = c;
            trajectoryLine.endColor   = new Color(c.r, c.g, c.b, 0f);
        }
        else
        {
            trajectoryLine.enabled = false;
        }
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

        if (currentHealth <= 0)
        {
            Debug.Log("[PlayerController] Health reached 0 – resetting to spawn position.");
            currentHealth = maxHealth;
            transform.position = spawnPosition;
            if (rb != null) rb.linearVelocity = Vector2.zero;
            isDashing = false; // cancel any in-flight dash
        }
        else
        {
            isInvincible       = true;
            invincibilityTimer = invincibilityDuration;
        }
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

    private Vector2 GetWorldMouseDirection()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[PlayerController] Camera.main is null \u2013 dash direction defaults to right. " +
                           "Ensure the Main Camera has the 'MainCamera' tag.");
            return Vector2.right;
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
        Vector2 dir = (Vector2)world - (Vector2)transform.position;
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
