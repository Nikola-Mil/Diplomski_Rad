// LevelGeneratorTests.cs
// Runs the real ProceduralLevelGenerator.GenerateLevel(seed) on a temporary
// tilemap for seeds 1..SEED_COUNT, reads the resulting grid back, and checks it:
//
//   1. Floor:     every column of both zones has the full-depth floor.
//   2. Floor path: every platform too low to walk under (clearance < player
//                 height) can be climbed onto from the floor, from either side —
//                 i.e. walking the floor can never dead-end.
//   3. Platforms: every platform can be reached from its neighbour with the
//                 basic jump, in both directions — the stronger claim.
//   4. Objects:   nothing is spawned somewhere the player cannot fit.
//
// "Can be reached" is decided against the jump the engine actually produces
// (JumpEnvelope), not against a formula. All numbers are written to the log
// with a [REPORT] prefix.
//
// Run: Window > General > Test Runner > EditMode, or headless (see README).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;
using PLG = ProceduralLevelGenerator;

public class LevelGeneratorTests
{
    private const int FIRST_SEED = 1;
    private const int SEED_COUNT = 500;

    private static readonly (string name, int start, int endExclusive)[] ZONES =
    {
        ("left",  PLG.PROC_LEFT_START_X,  PLG.PROC_LEFT_END_X),
        ("right", PLG.PROC_RIGHT_START_X, PLG.PROC_RIGHT_END_X),
    };

    private static float FloorY => PLG.FLOOR_Y + 1;   // world y of the floor surface

    // ── Tests ────────────────────────────────────────────────────────────────

    [Test]
    public void SameSeed_GivesIdenticalLevel_DifferentSeed_DoesNot()
    {
        using (new TempScene())
        {
            var tm = CreateGroundTilemap();
            string a = Snapshot(tm, 12345);
            string b = Snapshot(tm, 12345);
            string c = Snapshot(tm, 12346);
            Assert.AreEqual(a, b, "Same seed produced a different level.");
            Assert.AreNotEqual(a, c, "Different seeds produced the same level.");
        }
    }

    [Test] public void Floor_IsContinuous_InEveryRun()        => AssertNone(Results.floorFailures,   "floor gaps");
    [Test] public void FloorRoute_NeverDeadEnds_InEveryRun()  => AssertNone(Results.blockerFailures, "unclimbable floor obstacles");
    [Test] public void EveryPlatform_ReachableFromNeighbour() => AssertNone(Results.pairFailures,    "unreachable platform pairs");
    [Test] public void NoObject_SpawnsWherePlayerCannotFit()  => AssertNone(Results.pocketFailures,  "objects in pockets the player can't enter");

    private static void AssertNone(List<string> failures, string what) =>
        Assert.IsEmpty(failures, $"{failures.Count} {what} across seeds {FIRST_SEED}..{FIRST_SEED + SEED_COUNT - 1}. " +
                                 "First few:\n" + string.Join("\n", failures.Take(10)));

    // ── Evaluation (runs once, shared by the tests above) ────────────────────

    private class Evaluation
    {
        public readonly List<string> floorFailures   = new List<string>();
        public readonly List<string> blockerFailures = new List<string>();
        public readonly List<string> pairFailures    = new List<string>();
        public readonly List<string> pocketFailures  = new List<string>();
    }

    private static Evaluation results;
    private static Evaluation Results => results ?? (results = Evaluate());

    private static Evaluation Evaluate()
    {
        var family = JumpEnvelope.Family;   // measured first: it needs its own temporary scene
        var ev     = new Evaluation();
        var clock  = Stopwatch.StartNew();

        int platforms = 0, pairChecks = 0, blockerChecks = 0, notFromFloor = 0;
        var pairKinds      = new SortedDictionary<string, int>();   // "gap g, rise dh" → count
        var pickupsPerZone = new List<int>();
        int spikes = 0, enemies = 0, pickups = 0;
        var pocketsByKind  = new Dictionary<string, int> { { "Pickup", 0 }, { "Spike", 0 }, { "Enemy", 0 } };

        using (new TempScene())
        {
            var tm = CreateGroundTilemap();
            for (int seed = FIRST_SEED; seed < FIRST_SEED + SEED_COUNT; seed++)
            {
                PLG.GenerateLevel(seed);

                foreach (var zone in ZONES)
                {
                    // 1. Floor
                    for (int x = zone.start; x < zone.endExclusive; x++)
                        for (int dy = 0; dy < PLG.FLOOR_THICKNESS; dy++)
                            if (!tm.HasTile(new Vector3Int(x, PLG.FLOOR_Y - dy, 0)))
                                ev.floorFailures.Add($"seed {seed}: no floor tile at ({x},{PLG.FLOOR_Y - dy})");

                    var plats = ReadPlatforms(tm, zone.start, zone.endExclusive);
                    platforms += plats.Count;

                    for (int i = 0; i < plats.Count; i++)
                    {
                        Block p = plats[i];
                        var others = plats.Where((_, j) => j != i).ToList();

                        // 2. Floor route: platforms the player can't walk under must be climbable.
                        if (p.yMin - FloorY < PlayerPhysicsHarness.HEIGHT)
                        {
                            float leftFrom  = i > 0 ? plats[i - 1].xMax : zone.start;
                            float rightTo   = i < plats.Count - 1 ? plats[i + 1].xMin : zone.endExclusive;
                            blockerChecks += 2;
                            if (!JumpEnvelope.CanReach(leftFrom, p.xMin, FloorY, p, others, FloorY))
                                ev.blockerFailures.Add($"seed {seed} {zone.name}: can't climb {Describe(p)} from the floor on its left");
                            if (!CanReachMirrored(p.xMax, rightTo, FloorY, p, others))
                                ev.blockerFailures.Add($"seed {seed} {zone.name}: can't climb {Describe(p)} from the floor on its right");
                        }

                        // Informational: platforms not reachable straight from the floor.
                        bool fromFloor =
                            JumpEnvelope.CanReach(i > 0 ? plats[i - 1].xMax : zone.start, p.xMin, FloorY, p, others, FloorY) ||
                            CanReachMirrored(p.xMax, i < plats.Count - 1 ? plats[i + 1].xMin : zone.endExclusive, FloorY, p, others);
                        if (!fromFloor) notFromFloor++;

                        // 3. Platform → next platform, both directions.
                        if (i == plats.Count - 1) continue;
                        Block a = p, b = plats[i + 1];
                        var obstacles = plats.Where((_, j) => j != i + 1).ToList();
                        var obstaclesBack = plats.Where((_, j) => j != i).ToList();
                        string kind = $"gap {b.xMin - a.xMax:0}, height change {b.yMax - a.yMax:+0;-0}";
                        pairKinds[kind] = pairKinds.TryGetValue(kind, out int n) ? n + 1 : 1;
                        pairChecks += 2;
                        if (!JumpEnvelope.CanReach(a.xMin, a.xMax, a.yMax, b, obstacles, FloorY))
                            ev.pairFailures.Add($"seed {seed} {zone.name}: {Describe(a)} -> {Describe(b)}");
                        if (!CanReachMirrored(b.xMin, b.xMax, b.yMax, a, obstaclesBack))
                            ev.pairFailures.Add($"seed {seed} {zone.name}: {Describe(b)} -> {Describe(a)}");
                    }
                }

                // 4. Objects
                var zonePickups = new int[ZONES.Length];
                foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                {
                    if (!TryParseProcObject(go.name, out string kind, out int cx, out int cy)) continue;
                    if (kind == "Pickup") { pickups++; zonePickups[cx < 0 ? 0 : 1]++; }
                    else if (kind == "Spike") spikes++;
                    else enemies++;

                    // The player (1.1 tall) needs both cells above the surface tile empty.
                    bool pocket = tm.HasTile(new Vector3Int(cx, cy + 1, 0)) ||
                                  tm.HasTile(new Vector3Int(cx, cy + 2, 0));
                    if (pocket)
                    {
                        pocketsByKind[kind]++;
                        ev.pocketFailures.Add($"seed {seed}: {go.name} is under a platform with 1 cell of headroom");
                    }
                }
                pickupsPerZone.AddRange(zonePickups);

                if (seed % 50 == 0) UnityEditor.Undo.ClearAll();   // keep the undo stack from growing for 500 runs
            }
        }

        // ── Report ───────────────────────────────────────────────────────────
        Report($"Procedural generator evaluation: seeds {FIRST_SEED}..{FIRST_SEED + SEED_COUNT - 1} " +
               $"({SEED_COUNT} runs, {SEED_COUNT * ZONES.Length} zones), {clock.Elapsed.TotalSeconds:F1} s");
        Report($"  jump trajectories measured in-engine: {family.Count}");
        Report($"  platforms generated: {platforms} (avg {platforms / (float)(SEED_COUNT * ZONES.Length):F1} per zone)");
        Report($"  runs with a continuous floor: {Rate(SEED_COUNT, CountSeeds(ev.floorFailures))}");
        Report($"  floor obstacles climbable: {blockerChecks - ev.blockerFailures.Count}/{blockerChecks} checks");
        Report($"  neighbour pairs reachable: {pairChecks - ev.pairFailures.Count}/{pairChecks} checks (both directions)");
        Report($"  platforms NOT reachable directly from the floor: {notFromFloor}/{platforms} (informational)");
        foreach (var kv in pairKinds) Report($"    pair kind {kv.Key}: {kv.Value}");
        Report($"  objects: {pickups} pickups, {spikes} spikes, {enemies} enemies");
        Report($"  pickups per zone: min {pickupsPerZone.Min()}, mean {pickupsPerZone.Average():F1}, max {pickupsPerZone.Max()}");
        Report($"  objects in 1-cell pockets: pickups {pocketsByKind["Pickup"]}, spikes {pocketsByKind["Spike"]}, " +
               $"enemies {pocketsByKind["Enemy"]} (seeds affected: {ev.pocketFailures.Select(f => f.Split(':')[0]).Distinct().Count()}/{SEED_COUNT})");
        return ev;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool CanReachMirrored(float takeoffMin, float takeoffMax, float takeoffY, Block target, List<Block> obstacles) =>
        JumpEnvelope.CanReach(-takeoffMax, -takeoffMin, takeoffY, target.Mirrored,
                              obstacles.Select(o => o.Mirrored), FloorY);

    private static List<Block> ReadPlatforms(Tilemap tm, int start, int endExclusive)
    {
        var result = new List<Block>();
        foreach (int y in PLG.PLATFORM_HEIGHTS)
        {
            int runStart = int.MinValue;
            for (int x = start; x <= endExclusive; x++)
            {
                bool has = x < endExclusive && tm.HasTile(new Vector3Int(x, y, 0));
                if (has && runStart == int.MinValue) runStart = x;
                if (!has && runStart != int.MinValue)
                {
                    result.Add(new Block(runStart, x, y, y + 1));
                    runStart = int.MinValue;
                }
            }
        }
        result.Sort((a, b) => a.xMin.CompareTo(b.xMin));
        return result;
    }

    private static Tilemap CreateGroundTilemap()
    {
        var grid = new GameObject("Grid", typeof(Grid));
        var tmGO = new GameObject("GroundTilemap", typeof(Tilemap), typeof(TilemapRenderer));
        tmGO.transform.SetParent(grid.transform, false);
        return tmGO.GetComponent<Tilemap>();
    }

    private static string Snapshot(Tilemap tm, int seed)
    {
        PLG.GenerateLevel(seed);
        var parts = new List<string>();
        foreach (var zone in ZONES)
            for (int x = zone.start; x < zone.endExclusive; x++)
                for (int y = PLG.FLOOR_Y; y <= 3; y++)
                    if (tm.HasTile(new Vector3Int(x, y, 0))) parts.Add($"t{x},{y}");
        foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (go.name.StartsWith("Proc", StringComparison.Ordinal))
                parts.Add($"{go.name}@{go.transform.position}");
        parts.Sort(StringComparer.Ordinal);
        return string.Join(";", parts);
    }

    // "ProcPickup_12_-4" → ("Pickup", 12, -4); the numbers are the surface cell.
    private static bool TryParseProcObject(string name, out string kind, out int x, out int y)
    {
        kind = null; x = y = 0;
        if (!name.StartsWith("Proc", StringComparison.Ordinal)) return false;
        var parts = name.Substring(4).Split('_');
        return parts.Length == 3 && int.TryParse(parts[1], out x) && int.TryParse(parts[2], out y)
               && (kind = parts[0]) != null;
    }

    private static string Describe(Block b) => $"[x {b.xMin:0}..{b.xMax - 1:0}, surface y {b.yMax:0}]";

    private static int CountSeeds(List<string> failures) =>
        failures.Select(f => f.Split(':')[0]).Distinct().Count();

    private static string Rate(int total, int failed) =>
        $"{total - failed}/{total} ({100.0 * (total - failed) / total:F1}%)";

    internal static void Report(string line)
    {
        Debug.Log("[REPORT] " + line);
        TestContext.Progress.WriteLine(line);
    }
}
