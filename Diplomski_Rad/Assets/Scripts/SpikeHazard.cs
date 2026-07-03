// SpikeHazard.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices:
//   • Damage is applied via PlayerController.TakeDamage so invincibility frames
//     are respected — a spike pit cannot chain-kill in a single frame.
//   • The collider is always a trigger so the spike does not act as a physical
//     obstacle; it reads as "pass through and hurt" rather than "solid wall".
//   • No cooldown is stored here; PlayerController owns the invincibility state,
//     which is the right owner for that logic.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • No Collider2D at Awake: BoxCollider2D trigger added automatically.
//   • Player tag present but no PlayerController: logged as warning, not error,
//     since scene setup often has test objects tagged "Player" during development.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

public class SpikeHazard : MonoBehaviour
{
    [Tooltip("Damage dealt to the player on contact.")]
    public int damage = 1;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
        {
            Debug.LogWarning($"[SpikeHazard] No Collider2D on '{gameObject.name}'. " +
                             "Adding a BoxCollider2D trigger automatically. " +
                             "Resize it in the Inspector to match the spike sprite.");
            var bc = gameObject.AddComponent<BoxCollider2D>();
            bc.isTrigger = true;
        }
        else if (!col.isTrigger)
        {
            Debug.LogWarning($"[SpikeHazard] Collider2D on '{gameObject.name}' was not a trigger. " +
                             "Setting isTrigger = true so the player passes through the spike.");
            col.isTrigger = true;
        }
    }

    // ── Trigger ──────────────────────────────────────────────────────────────

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null)
        {
            Debug.LogWarning("[SpikeHazard] OnTriggerEnter2D received a null Collider2D.");
            return;
        }

        if (!other.CompareTag("Player")) return;

        var pc = other.GetComponent<PlayerController>();
        if (pc == null)
        {
            Debug.LogWarning($"[SpikeHazard] '{other.gameObject.name}' is tagged 'Player' " +
                             "but has no PlayerController – damage cannot be applied. " +
                             "Ensure PlayerController.cs has compiled.");
            return;
        }

        pc.TakeDamage(damage);
    }
}
