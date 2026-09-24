// PlayerMovementTests.cs
// In-engine measurements of the player's movement (the numbers the thesis
// quotes) and of spike-hit registration. See PlayerPhysicsHarness for how the
// real controller is driven headlessly.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PLG = ProceduralLevelGenerator;

public class PlayerMovementTests
{
    private static void Report(string line) => LevelGeneratorTests.Report(line);

    [Test]
    public void JumpEnvelope_CoversGeneratorLimits()
    {
        var running  = JumpEnvelope.Family.First(t => t.Name == "running jump, key held");
        var standing = JumpEnvelope.Family.First(t => t.Name == "standing jump, no key");

        float g    = -Physics2D.gravity.y;
        float gs   = JumpEnvelope.GravityScale;
        float v0   = JumpEnvelope.JumpForce;
        float hFormula = v0 * v0 / (2f * g * gs);

        Report("Jump envelope (measured in-engine, 50 Hz, real PlayerController + Box2D):");
        Report($"  config: jumpForce {v0} (written straight to linearVelocity.y, so mass {JumpEnvelope.Mass} is irrelevant), " +
               $"gravity {Physics2D.gravity.y}, gravityScale {gs}, moveSpeed {JumpEnvelope.MoveSpeed}, " +
               $"fixedDeltaTime {Time.fixedDeltaTime}");
        Report($"  closed-form apex v0^2/(2*g*gs) = {hFormula:F3}; closed-form airtime 2*v0/(g*gs) = {2f * v0 / (g * gs):F3} s");
        Report($"  measured apex (running): {JumpEnvelope.Apex(running):F3} at {JumpEnvelope.ApexTime(running):F2} s; " +
               $"(standing): {JumpEnvelope.Apex(standing):F3}");
        foreach (float dy in new[] { 2f, 1f, 0f, -1f, -2f })
        {
            var (dx, t) = JumpEnvelope.DescendingCrossing(running, dy);
            Report($"  running jump passes back down through {dy:+0;-0;0}: after {t:F3} s, {dx:F2} units forward");
        }

        // What the generator needs: rise of at most 2 between neighbouring
        // platforms (heights differ by 1 or 2), gaps of at most MAX_GAP.
        int maxRise = PLG.PLATFORM_HEIGHTS.Max() - PLG.PLATFORM_HEIGHTS.Min();
        Assert.Greater(JumpEnvelope.Apex(running), maxRise, "Jump can't rise between the lowest and highest platform.");
        Assert.Greater(JumpEnvelope.DescendingCrossing(running, maxRise).dx, PLG.MAX_GAP,
                       "Jump can't cross MAX_GAP while rising the full platform height difference.");
    }

    // ── Spikes ───────────────────────────────────────────────────────────────

    private struct Tally { public int cases, seen, registered, missed, invisibleHit; }

    [Test]
    public void Spike_RegistersEveryVisibleTouch_WalkingJumpingAndDashing()
    {
        using (var h = new PlayerPhysicsHarness())
        {
            h.AddBlock(-40f, 40f, -5f, 0f);   // floor surface at y = 0
            // Same factory as the scene (flush with the floor: centre 0.2 above it).
            var spike    = BuildGameScene.SpawnSpikeHazard("Spike", new Vector3(0f, 0.2f, 0f));
            var spikeCol = spike.GetComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();

            var variants = new[]
            {
                ("collider as currently saved in PlatformerScene (0.0001 x 0.0001)", new Vector2(0.0001f, 0.0001f)),
                ("collider from fixed SpawnSpikeHazard (1 x 1 local = 0.4 x 0.4 world)", spikeCol.size),
            };

            Tally fixedTotal = default;
            foreach (var (label, size) in variants)
            {
                spikeCol.size = size;
                Physics2D.SyncTransforms();
                Report($"Spike hits — {label}:");
                var all = new Tally();
                foreach (var (scenario, tally) in RunSpikeScenarios(h, spikeCol))
                {
                    Report($"  {scenario,-28} cases {tally.cases,4} | visibly touched {tally.seen,4} | " +
                           $"hit registered {tally.registered,4} | MISSED {tally.missed,4} | hit without visible touch {tally.invisibleHit,3}");
                    all.cases += tally.cases; all.seen += tally.seen; all.registered += tally.registered;
                    all.missed += tally.missed; all.invisibleHit += tally.invisibleHit;
                }
                Report($"  {"TOTAL",-28} cases {all.cases,4} | visibly touched {all.seen,4} | missed {all.missed} " +
                       $"({(all.seen > 0 ? 100.0 * all.missed / all.seen : 0):F1}% of visible touches)");
                fixedTotal = all;
            }
            Assert.AreEqual(0, fixedTotal.missed, "Fixed spike collider still misses visible touches.");
        }
    }

    private static IEnumerable<(string, Tally)> RunSpikeScenarios(PlayerPhysicsHarness h, BoxCollider2D spike)
    {
        const float SPIKE_HALF = 0.2f, SPIKE_Y = 0.2f, PLAYER_HALF_SPRITE = 0.5f;
        bool seen = false, hit = false;
        Action check = () =>
        {
            Vector2 c = h.Rb.position;
            seen |= Mathf.Abs(c.x) < PLAYER_HALF_SPRITE + SPIKE_HALF &&
                    Mathf.Abs(c.y - SPIKE_Y) < PLAYER_HALF_SPRITE + SPIKE_HALF;
            // Geometric overlap of the two colliders after the physics step —
            // the same test Box2D uses to decide a trigger contact.
            hit  |= Physics2D.Distance(h.Col, spike).isOverlapped;
        };
        Func<Tally, Tally> record = t =>
        {
            t.cases++;
            if (seen) t.seen++;
            if (hit) t.registered++;
            if (seen && !hit) t.missed++;
            if (hit && !seen) t.invisibleHit++;
            seen = hit = false;
            return t;
        };
        Action reset = () =>
        {
            h.Pc.currentElement = PlayerController.CreateNeutralElement();
            h.Rb.gravityScale   = 4.5f;
            seen = hit = false;
        };

        // Walking over it at full speed, 20 different sub-step phases.
        var walk = new Tally();
        for (int k = 0; k < 20; k++)
        {
            reset();
            h.Place(-4f + k * 0.013f, 0f, new Vector2(h.Pc.moveSpeed, 0f));
            for (int s = 0; s < 40; s++) { h.Step(1f); check(); }
            walk = record(walk);
        }
        yield return ("walk across", walk);

        // Running jumps taking off at many distances before it (hop over it, land on/near it).
        var jump = new Tally();
        for (float x = -10f; x <= 1f; x += 0.1f)
        {
            reset();
            h.Place(x, 0f, new Vector2(h.Pc.moveSpeed, 0f));
            h.Step(1f);
            h.Jump();
            for (int s = 0; s < 60; s++) { h.Step(1f); check(); }
            jump = record(jump);
        }
        yield return ("running jumps", jump);

        // Horizontal dashes straight across it, per element, at heights from
        // standing on the floor to just above the spike, 10 phases each.
        var elements = new[]
        {
            PlayerController.CreateNeutralElement(), PlayerController.CreateEarthElement(),
            PlayerController.CreateFireElement(),    PlayerController.CreateWaterElement(),
            PlayerController.CreateAirElement(),
        };
        foreach (var e in elements)
        {
            var dash = new Tally();
            for (float feet = 0f; feet <= 0.6001f; feet += 0.05f)
                for (int k = 0; k < 10; k++)
                {
                    reset();
                    h.Pc.currentElement = e;
                    h.Place(-e.dashMaxDistance * 0.5f + k * 0.055f, feet, Vector2.zero);
                    h.Dash(Vector2.right, e.dashMaxDistance, 15, check);
                    dash = record(dash);
                }
            yield return ($"dash {e.elementName} ({e.dashSpeed} u/s)", dash);
        }

        // Air dashes angled down onto it from above, several angles and offsets.
        var air = PlayerController.CreateAirElement();
        var down = new Tally();
        foreach (float deg in new[] { 15f, 30f, 45f, 60f })
            for (float off = -1f; off <= 1.001f; off += 0.1f)
            {
                reset();
                h.Pc.currentElement = air;
                var dir   = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), -Mathf.Sin(deg * Mathf.Deg2Rad));
                var start = new Vector2(off, 0.2f) - dir * (air.dashMaxDistance * 0.6f);
                h.Place(start.x, Mathf.Max(start.y - PlayerPhysicsHarness.HEIGHT * 0.5f, 0.3f), Vector2.zero);
                h.Dash(dir, air.dashMaxDistance, 15, check);
                down = record(down);
            }
        yield return ("dash Air, angled down", down);
    }
}
