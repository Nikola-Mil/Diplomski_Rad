// JumpEnvelope.cs
// Measures the player's real jump in the engine (PlayerPhysicsHarness) and
// answers "can the player get from here to there with the basic jump alone?"
// against that measurement, not against a closed-form estimate.
//
// Why measured and not derived: the closed-form h = v0²/(2g) assumes one
// constant gravity. The controller switches gravityScale every physics tick
// (x0.7 near the apex, x2.5 while falling), and Box2D integrates in discrete
// 20 ms steps, so the real arc differs from the formula in both height and
// airtime, and not in the same direction.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

internal sealed class Trajectory
{
    public string Name;
    // Offsets from the take-off point after every physics tick:
    // x = horizontal travel of the collider centre, y = rise of the collider bottom.
    public readonly List<Vector2> Points = new List<Vector2> { Vector2.zero };
}

/// <summary>Axis-aligned solid rectangle in world units (one platform or a tile run).</summary>
internal struct Block
{
    public float xMin, xMax, yMin, yMax;
    public Block(float xMin, float xMax, float yMin, float yMax)
    { this.xMin = xMin; this.xMax = xMax; this.yMin = yMin; this.yMax = yMax; }
    public Block Mirrored => new Block(-xMax, -xMin, yMin, yMax);
}

internal static class JumpEnvelope
{
    private const int MAX_STEPS = 250;

    private static List<Trajectory> cached;

    // Configuration of the player the family was measured on.
    public static float GravityScale, JumpForce, MoveSpeed, Mass;

    /// <summary>
    /// Every trajectory the basic jump can produce under simple held-key
    /// control: running or standing take-off, then holding, releasing or
    /// reversing the direction key at some tick. Always rightward; leftward
    /// jumps are checked by mirroring the level instead.
    /// </summary>
    public static List<Trajectory> Family
    {
        get
        {
            if (cached != null) return cached;
            using (var h = new PlayerPhysicsHarness())
            {
                GravityScale = h.Rb.gravityScale;
                Mass         = h.Rb.mass;
                JumpForce    = h.Pc.jumpForce;
                MoveSpeed    = h.Pc.moveSpeed;

                // Take-off ledge; its collider is switched off one tick after
                // take-off so the rest of the arc is free flight into the void.
                var ledge = h.AddBlock(-60f, -19.5f, -5f, 0f).GetComponent<Collider2D>();
                float run = h.Pc.moveSpeed;

                var family = new List<Trajectory>
                {
                    Measure(h, ledge, "running jump, key held", run, s => 1f),
                    Measure(h, ledge, "standing jump, no key",  0f,  s => 0f),
                };
                for (int k = 0; k <= 40; k += 2)
                {
                    int kk = k;
                    family.Add(Measure(h, ledge, $"running, release @{kk}", run, s => s < kk ? 1f :  0f));
                    family.Add(Measure(h, ledge, $"running, reverse @{kk}", run, s => s < kk ? 1f : -1f));
                    family.Add(Measure(h, ledge, $"standing, press @{kk}",  0f,  s => s < kk ? 0f :  1f));
                }
                cached = family;
            }
            return cached;
        }
    }

    private static Trajectory Measure(PlayerPhysicsHarness h, Collider2D ledge, string name,
                                      float startSpeed, Func<int, float> input)
    {
        ledge.enabled = true;
        float settleInput = startSpeed > 0f ? 1f : 0f;
        h.Place(-22f, 0f, new Vector2(startSpeed, 0f));
        h.Step(settleInput);
        h.Step(settleInput);

        var   t  = new Trajectory { Name = name };
        float x0 = h.Rb.position.x;
        float y0 = h.Feet;
        h.Jump();
        for (int s = 0; s < MAX_STEPS; s++)
        {
            h.Step(input(s));
            if (s == 0) ledge.enabled = false;
            var p = new Vector2(h.Rb.position.x - x0, h.Feet - y0);
            t.Points.Add(p);
            if (p.y < -8f) break;
        }
        ledge.enabled = true;
        return t;
    }

    // ── Summary numbers for the running, key-held jump ───────────────────────

    public static float Apex(Trajectory t) => t.Points.Max(p => p.y);

    public static float ApexTime(Trajectory t)
    {
        int i = t.Points.FindIndex(p => Mathf.Approximately(p.y, Apex(t)));
        return i * Time.fixedDeltaTime;
    }

    /// <summary>
    /// Horizontal travel and elapsed time when the falling arc passes back down
    /// through height dy (linear interpolation between ticks).
    /// </summary>
    public static (float dx, float time) DescendingCrossing(Trajectory t, float dy)
    {
        for (int i = 1; i < t.Points.Count; i++)
        {
            Vector2 a = t.Points[i - 1], b = t.Points[i];
            if (a.y >= dy && b.y < dy && b.y < a.y)
            {
                float f = (a.y - dy) / (a.y - b.y);
                return (Mathf.Lerp(a.x, b.x, f), (i - 1 + f) * Time.fixedDeltaTime);
            }
        }
        return (float.NaN, float.NaN);
    }

    // ── Reachability ─────────────────────────────────────────────────────────

    private static readonly Dictionary<string, bool> memo = new Dictionary<string, bool>();

    /// <summary>
    /// True if, standing anywhere on [takeoffMin, takeoffMax] at height takeoffY,
    /// some measured trajectory lands the player on top of target (collider
    /// centre over it) without first touching any obstacle or dropping below
    /// floorY. Movement is to the right; mirror the geometry for leftward.
    /// </summary>
    public static bool CanReach(float takeoffMin, float takeoffMax, float takeoffY,
                                Block target, IEnumerable<Block> obstacles, float floorY)
    {
        // Everything is relative to the take-off segment, so identical local
        // geometry (which repeats a lot across seeds) is only solved once.
        var near = obstacles.Where(o => o.xMax > takeoffMin - 12f && o.xMin < target.xMax + 12f).ToList();
        string key = string.Join("|", new[] { target }.Concat(near)
                           .Select(b => $"{b.xMin - takeoffMin:F2},{b.xMax - takeoffMin:F2},{b.yMin - takeoffY:F2},{b.yMax - takeoffY:F2}"))
                     + $"|{takeoffMax - takeoffMin:F2}|{floorY - takeoffY:F2}";
        if (memo.TryGetValue(key, out bool known)) return known;

        bool ok = false;
        foreach (var t in Family)
        {
            // Nearest take-off points to the target first: usually the ones that work.
            for (float x0 = takeoffMax; x0 >= takeoffMin - 1e-4f && !ok; x0 -= 0.05f)
                ok = Flies(t, x0, takeoffY, target, near, floorY);
            if (ok) break;
        }
        memo[key] = ok;
        return ok;
    }

    private static bool Flies(Trajectory t, float x0, float y0, Block target, List<Block> obstacles, float floorY)
    {
        const int   SUB = 8;
        const float EPS = 1e-3f;
        Vector2 prev = new Vector2(x0, y0);
        for (int i = 1; i < t.Points.Count; i++)
        {
            Vector2 cur = new Vector2(x0 + t.Points[i].x, y0 + t.Points[i].y);
            for (int s = 1; s <= SUB; s++)
            {
                Vector2 p = Vector2.Lerp(prev, cur, s / (float)SUB);
                Vector2 q = Vector2.Lerp(prev, cur, (s - 1) / (float)SUB);
                if (Overlaps(p, target, EPS))
                    // Landed only if it came down onto the top face with its centre over the block.
                    return q.y >= target.yMax - EPS && p.x >= target.xMin && p.x <= target.xMax;
                foreach (var o in obstacles)
                    if (Overlaps(p, o, EPS)) return false;
                if (p.y < floorY - EPS) return false;
            }
            prev = cur;
        }
        return false;
    }

    private static bool Overlaps(Vector2 feetCentre, Block b, float eps) =>
        feetCentre.x + PlayerPhysicsHarness.HALF_WIDTH > b.xMin + eps &&
        feetCentre.x - PlayerPhysicsHarness.HALF_WIDTH < b.xMax - eps &&
        feetCentre.y + PlayerPhysicsHarness.HEIGHT     > b.yMin + eps &&
        feetCentre.y                                   < b.yMax - eps;
}
