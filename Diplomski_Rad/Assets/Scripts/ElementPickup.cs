// ElementPickup.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices:
//   • The pickup stores a full ElementStats value rather than an enum index.
//     This makes it data-driven: each prefab/instance can configure any stats
//     without needing a central registry.
//   • Floating animation uses Mathf.Sin with a per-instance phase offset so a
//     row of pickups doesn't bob in perfect sync — looks more organic.
//   • Respawn coroutine disables both the renderer AND the collider, preventing
//     invisible-pickup exploits.  It re-enables both on respawn.
//   • The diamond sprite is procedurally generated so no external art is required.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • No SpriteRenderer: added automatically with a warning.
//   • No Collider2D: a CircleCollider2D trigger is added automatically.
//   • PlayerController.SetElement throws: caught and logged; pickup is not
//     consumed so the player can retry.
//   • respawnTime = 0: pickup is hidden permanently (no respawn coroutine started).
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections;
using UnityEngine;

public class ElementPickup : MonoBehaviour
{
    [Header("Element")]
    [Tooltip("Element granted to the player on contact.  Set via Inspector or preset factory.")]
    public ElementStats elementToGive;

    [Header("Respawn")]
    [Tooltip("Seconds before the pickup reappears.  Set 0 to disable respawn (pickup is single-use).")]
    public float respawnTime = 5f;

    // ── Private state ────────────────────────────────────────────────────────
    private SpriteRenderer sr;
    private Collider2D     pickupCollider;
    private Vector3        startPos;
    private float          floatPhase;    // per-instance offset to desync floating
    private bool           collected;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            Debug.LogWarning($"[ElementPickup] No SpriteRenderer on '{gameObject.name}'. Adding one.");
            sr = gameObject.AddComponent<SpriteRenderer>();
        }

        pickupCollider = GetComponent<Collider2D>();
        if (pickupCollider == null)
        {
            Debug.LogWarning($"[ElementPickup] No Collider2D on '{gameObject.name}'. " +
                             "Adding CircleCollider2D trigger.");
            var cc = gameObject.AddComponent<CircleCollider2D>();
            cc.isTrigger = true;
            cc.radius    = 0.4f;
            pickupCollider = cc;
        }

        // Random phase offset prevents synchronized bobbing across multiple pickups.
        floatPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        startPos   = transform.position;

        ApplyVisual();
    }

    private void Update()
    {
        if (collected) return;

        // Smooth floating bob
        float y = startPos.y + Mathf.Sin(Time.time * 2f + floatPhase) * 0.15f;
        transform.position = new Vector3(startPos.x, y, startPos.z);

        // Slow spin for visual distinction between elements
        transform.Rotate(0f, 0f, 50f * Time.deltaTime);
    }

    // ── Pickup trigger ───────────────────────────────────────────────────────

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected)       return;
        if (other == null)   return;
        if (!other.CompareTag("Player")) return;

        var pc = other.GetComponent<PlayerController>();
        if (pc == null)
        {
            Debug.LogError($"[ElementPickup] Player-tagged object '{other.gameObject.name}' " +
                           "has no PlayerController – element cannot be granted.");
            return;
        }

        try
        {
            pc.SetElement(elementToGive);
            Debug.Log($"[ElementPickup] Player received element '{elementToGive.elementName}'.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ElementPickup] PlayerController.SetElement threw: {e.Message}\n" +
                           "The pickup has NOT been consumed.");
            return; // do not mark as collected; let player retry
        }

        collected = true;

        if (respawnTime > 0f)
            StartCoroutine(RespawnAfterDelay());
        else
            gameObject.SetActive(false);
    }

    // ── Respawn ──────────────────────────────────────────────────────────────

    private IEnumerator RespawnAfterDelay()
    {
        // Disable visual and collision immediately; keep GameObject active so
        // the coroutine keeps running.
        if (sr             != null) sr.enabled             = false;
        if (pickupCollider != null) pickupCollider.enabled = false;

        yield return new WaitForSeconds(respawnTime);

        collected = false;
        if (sr             != null) sr.enabled             = true;
        if (pickupCollider != null) pickupCollider.enabled = true;

        // Reset position (it may have drifted slightly during the float animation).
        transform.position = startPos;

        Debug.Log($"[ElementPickup] '{elementToGive.elementName}' pickup respawned.");
    }

    // ── Visual ───────────────────────────────────────────────────────────────

    private void ApplyVisual()
    {
        if (sr == null) return;

        Color c = elementToGive.elementColor != default
            ? elementToGive.elementColor
            : Color.white;

        sr.sprite = CreateDiamondSprite(c);
        sr.color  = c;
    }

    /// <summary>Creates a 16×16 diamond-shaped sprite for the pickup icon.</summary>
    private static Sprite CreateDiamondSprite(Color color)
    {
        const int size = 16;
        var tex  = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - half);
                float dy = Mathf.Abs(y + 0.5f - half);
                tex.SetPixel(x, y, dx + dy <= half ? color : Color.clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
