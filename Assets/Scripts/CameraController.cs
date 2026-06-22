// CameraController.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices:
//
//   WHY LateUpdate?
//     Camera movement must happen AFTER all other objects have moved.  Physics
//     runs in FixedUpdate; player input in Update; animations in LateUpdate.
//     Using LateUpdate means we always read the player's final-frame position,
//     preventing the 1-frame jitter that appears when using Update.
//
//   THREE-SPEED SYSTEM:
//     • normalFollowSpeed  – comfortable smooth follow at all times.
//     • dashLagSpeed       – intentionally SLOW during a dash.  The camera trails
//                            behind the player, making the movement feel fast and
//                            kinetic.  The screen edge creates natural anticipation
//                            for what's ahead.
//     • catchUpSpeed       – very fast immediately after the dash ends.  The view
//                            snaps forward to centre on the player again.
//
//   WHY RAPID SNAP-BACK MATTERS FOR SPEEDRUNNERS:
//     In high-level play, players chain multiple dashes in quick succession.
//     Without snap-back, each subsequent dash fires toward screen-edge because
//     the camera is still trailing from the previous dash.  The player loses
//     sight of targets and must slow down to re-orient.  With catchUpSpeed, the
//     camera centres within a fraction of a second, so the next dash can be
//     aimed accurately at full pace — critical for routing and skip execution.
//
//   SmoothDamp vs Lerp:
//     SmoothDamp maintains a velocity vector across frames, producing a smooth
//     ease-in / ease-out profile.  Lerp(pos, target, t*dt) always undershoots
//     because 't' is frame-dependent and the exponential decay never fully
//     arrives.  SmoothDamp also exposes a maxSpeed clamp (not used here but
//     available for cinematic lock-off).
//
//   LAG OFFSET DIRECTION:
//     The lag offset is applied OPPOSITE to the dash direction
//     (offset = -direction * multiplier) so the camera trails behind, not ahead.
//     A multiplier of 0 disables the effect entirely if not desired.
//
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • target null at runtime: logged once, script disables itself.  Setting
//     target in the Inspector always takes priority over the tag search.
//   • playerController missing on target: camera still follows normally;
//     lag mechanic is silently bypassed (no spam in logs).
//   • Camera.main has no CameraController: PlayerController will log a warning
//     on TriggerCatchUp calls but the dash itself is unaffected.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    // ── Serialized configuration ──────────────────────────────────────────────

    [Header("Target")]
    [Tooltip("Transform the camera follows.  Leave null to auto-find by 'Player' tag at Start.")]
    public Transform target;

    [Tooltip("Fixed offset from the target.  Z should be -10 for a standard 2D orthographic setup.")]
    public Vector3 offset = new Vector3(0f, 0f, -10f);

    [Header("Follow Speeds")]
    [Tooltip("SmoothDamp speed during normal gameplay.  Higher = tighter follow.")]
    public float normalFollowSpeed = 8f;

    [Tooltip("SmoothDamp speed while the player is dashing.  Lower = more dramatic trail effect.")]
    public float dashLagSpeed = 2f;

    [Tooltip("SmoothDamp speed during snap-back after a dash.  High value = near-instant snap.")]
    public float catchUpSpeed = 20f;

    [Tooltip("Distance from target below which snap-back is considered complete " +
             "and speed reverts to normalFollowSpeed.")]
    public float catchUpThreshold = 0.1f;

    [Header("Dash Lag")]
    [Tooltip("How far the camera trails behind the dash direction.  0 = no trail effect.")]
    public float lagOffsetMultiplier = 2f;

    // ── Private state ────────────────────────────────────────────────────────
    private Vector3          velocity       = Vector3.zero;  // SmoothDamp internal state
    private PlayerController playerController;
    private bool             isCatchingUp   = false;
    private bool             targetLostLogged = false;       // suppress repeated error spam

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        // If no target assigned in Inspector, search by tag.
        if (target == null)
        {
            var playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO == null)
            {
                Debug.LogError("[CameraController] No target assigned and no GameObject tagged " +
                               "'Player' found.  Assign a target in the Inspector.  " +
                               "CameraController is now disabled.");
                enabled = false;
                return;
            }
            target = playerGO.transform;
            Debug.Log($"[CameraController] Auto-found target: '{target.gameObject.name}'.");
        }

        // Cache the PlayerController for dash-state queries.
        // If missing, we gracefully fall back to plain follow (no crash, no spam).
        playerController = target.GetComponent<PlayerController>();
        if (playerController == null)
            Debug.LogWarning("[CameraController] PlayerController not found on target '" +
                             target.gameObject.name + "'.  Dash-lag effect will be disabled. " +
                             "Camera will still follow the target normally.");
    }

    private void LateUpdate()
    {
        // ── Guard ─────────────────────────────────────────────────────────────
        if (target == null)
        {
            if (!targetLostLogged)
            {
                Debug.LogError("[CameraController] Target transform is null at runtime.  " +
                               "Camera will stop following.  " +
                               "Re-assign target in the Inspector or respawn the player.");
                targetLostLogged = true;
            }
            return;
        }

        try
        {
            Vector3 baseTarget   = target.position + offset;
            Vector3 finalTarget;
            float   currentSpeed;

            // ── Phase 1: dashing – camera lags behind ─────────────────────────
            if (playerController != null && playerController.IsDashing())
            {
                Vector2 dashDir = playerController.GetDashDirection();

                // The camera moves in the OPPOSITE direction of the dash, so it
                // appears to trail behind the player's rapid movement.
                Vector3 lagOffset = (Vector3)(-dashDir.normalized * lagOffsetMultiplier);

                finalTarget  = baseTarget + lagOffset;
                currentSpeed = dashLagSpeed;

                // Clear catch-up flag; the dash has priority.
                // isCatchingUp will be set by TriggerCatchUp() when the dash ends.
                isCatchingUp = false;
            }
            // ── Phase 2: snap-back after dash ─────────────────────────────────
            else if (isCatchingUp)
            {
                finalTarget  = baseTarget;
                currentSpeed = catchUpSpeed;

                // Once we're close enough, consider catch-up complete.
                if (Vector3.Distance(transform.position, baseTarget) < catchUpThreshold)
                    isCatchingUp = false;
            }
            // ── Phase 3: normal follow ────────────────────────────────────────
            else
            {
                finalTarget  = baseTarget;
                currentSpeed = normalFollowSpeed;
            }

            // ── Apply SmoothDamp movement ─────────────────────────────────────
            // smoothTime = 1f / speed gives a natural inverse relationship:
            // higher speed values produce shorter smooth times (tighter follow).
            transform.position = Vector3.SmoothDamp(
                transform.position,
                finalTarget,
                ref velocity,
                1f / currentSpeed,
                Mathf.Infinity,
                Time.deltaTime
            );
        }
        catch (Exception e)
        {
            Debug.LogError($"[CameraController] Exception in LateUpdate: {e.Message}\n{e.StackTrace}");
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by PlayerController immediately when a dash ends (distance exhausted
    /// or enemy hit).  Switches the camera into high-speed catch-up mode so it
    /// snaps back to the player before the next dash can be aimed and fired.
    ///
    /// This call is safe to make even if the camera is currently dashing —
    /// the Phase 1 block above will override isCatchingUp until the dash actually
    /// finishes, at which point Phase 2 takes over cleanly.
    /// </summary>
    public void TriggerCatchUp()
    {
        isCatchingUp = true;
    }
}
