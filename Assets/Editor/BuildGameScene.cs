// BuildGameScene.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices:
//   • ObjectFactory.CreateGameObject is used throughout instead of new GameObject()
//     because it registers every creation in the Undo stack and marks the scene
//     dirty automatically — safer for Editor tooling.
//   • Every major step lives in its own try-catch so a partially-built scene is
//     always better than no scene at all.  Each catch prints a precise message and
//     continues to the next step.
//   • UI elements are NOT wired with Inspector drag-references; UIManager.cs
//     resolves them by name at runtime (Play mode), which keeps the Editor script
//     decoupled from MonoBehaviour state.
//   • When a GroundTile asset is missing, a temporary 16×16 white square is
//     generated with Sprite.Create and saved as Assets/Tiles/GroundTile.asset.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • Missing tile asset → auto-generated fallback sprite tile is used.
//   • Script types that haven't compiled yet → AddComponent will silently fail;
//     run the menu item again after a full recompile.
//   • Scenes folder absent → created by System.IO.Directory.CreateDirectory.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public static class BuildGameScene
{
    [MenuItem("Tools/Build Platformer Scene")]
    public static void Build()
    {
        // ── New empty scene ───────────────────────────────────────────────────
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 1. Main Camera ────────────────────────────────────────────────────
        try
        {
            var camGO = ObjectFactory.CreateGameObject("Main Camera",
                typeof(Camera), typeof(AudioListener));
            camGO.tag = "MainCamera";
            var cam = camGO.GetComponent<Camera>();
            if (cam == null) throw new Exception("Camera component not found after creation.");
            cam.orthographic      = true;
            cam.orthographicSize  = 5f;
            cam.backgroundColor   = new Color(0.1f, 0.1f, 0.2f, 1f);
            cam.transform.position = new Vector3(0f, 0f, -10f);
            Debug.Log("[BuildGameScene] Main Camera created.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] Step 1 (Main Camera) failed: {e.Message}\n{e.StackTrace}");
        }

        // ── 2. Grid + Tilemap ─────────────────────────────────────────────────
        try
        {
            var gridGO = ObjectFactory.CreateGameObject("Grid", typeof(Grid));
            var tilemapGO = ObjectFactory.CreateGameObject("GroundTilemap",
                typeof(Tilemap), typeof(TilemapRenderer), typeof(TilemapCollider2D));
            tilemapGO.transform.SetParent(gridGO.transform, false);

            var groundTilemap = tilemapGO.GetComponent<Tilemap>();
            if (groundTilemap == null) throw new Exception("Tilemap component missing on GroundTilemap.");

            // Attempt to load an existing tile; fall back to auto-generated one.
            Tile groundTile = AssetDatabase.LoadAssetAtPath<Tile>("Assets/Tiles/GroundTile.asset");
            if (groundTile == null)
            {
                Debug.LogWarning("[BuildGameScene] Assets/Tiles/GroundTile.asset not found – " +
                                 "creating a temporary square sprite tile.");
                groundTile = CreateFallbackTile();
            }

            if (groundTile != null)
            {
                for (int x = -15; x <= 15; x++)
                    groundTilemap.SetTile(new Vector3Int(x, -4, 0), groundTile);
                Debug.Log("[BuildGameScene] Ground tiles placed (x = -15 to 15, y = -4).");
            }
            else
            {
                Debug.LogError("[BuildGameScene] Could not obtain a fallback tile; " +
                               "ground row will be empty. Create Assets/Tiles/GroundTile.asset manually.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] Step 2 (Grid/Tilemap) failed: {e.Message}\n{e.StackTrace}");
        }

        // ── 3. Player ─────────────────────────────────────────────────────────
        try
        {
            var playerGO = ObjectFactory.CreateGameObject("Player",
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D));
            playerGO.tag = "Player";
            playerGO.transform.position = new Vector3(0f, -3f, 0f);

            var sr = playerGO.GetComponent<SpriteRenderer>();
            if (sr == null) throw new Exception("SpriteRenderer missing on Player.");
            sr.sprite = CreateSquareSprite(new Color(0.3f, 0.8f, 1f));

            var rb = playerGO.GetComponent<Rigidbody2D>();
            if (rb == null) throw new Exception("Rigidbody2D missing on Player.");
            rb.gravityScale = 3f;
            rb.constraints  = RigidbodyConstraints2D.FreezeRotation;

            // PlayerController – compiled separately; AddComponent returns null if not found.
            var pc = playerGO.AddComponent<PlayerController>();
            if (pc == null)
                Debug.LogWarning("[BuildGameScene] PlayerController could not be added. " +
                                 "Ensure Assets/Scripts/PlayerController.cs has compiled.");
            else
            {
                // Set Air as starting element so the dash works immediately without a pickup.
                // Air is the safest default: it never stops on enemy contact, preventing
                // accidental blocking inside an enemy hitbox.
                pc.currentElement = PlayerController.CreateAirElement();
            }

            // ScarfRoot – empty child at local offset (0.3, -0.2)
            var scarfRoot = ObjectFactory.CreateGameObject("ScarfRoot");
            scarfRoot.transform.SetParent(playerGO.transform, false);
            scarfRoot.transform.localPosition = new Vector3(0.3f, -0.2f, 0f);

            var sc = scarfRoot.AddComponent<ScarfController>();
            if (sc == null)
                Debug.LogWarning("[BuildGameScene] ScarfController could not be added. " +
                                 "Ensure Assets/Scripts/ScarfController.cs has compiled.");

            Debug.Log("[BuildGameScene] Player created with ScarfRoot.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] Step 3 (Player) failed: {e.Message}\n{e.StackTrace}");
        }

        // ── 4. Parallax Backgrounds ───────────────────────────────────────────
        try
        {
            CreateParallaxLayer("ParallaxFar",  new Vector3(0f, 0f, 5f), new Vector2(0.1f, 0f),
                new Color(0.05f, 0.05f, 0.2f));
            CreateParallaxLayer("ParallaxMid",  new Vector3(0f, 0f, 4f), new Vector2(0.3f, 0f),
                new Color(0.07f, 0.07f, 0.3f));
            CreateParallaxLayer("ParallaxNear", new Vector3(0f, 0f, 3f), new Vector2(0.6f, 0f),
                new Color(0.1f,  0.1f,  0.4f));
            Debug.Log("[BuildGameScene] Parallax layers created (far/mid/near).");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] Step 4 (Parallax) failed: {e.Message}\n{e.StackTrace}");
        }

        // ── 5. UI Canvas ──────────────────────────────────────────────────────
        try
        {
            BuildUICanvas();
            Debug.Log("[BuildGameScene] UI Canvas created.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] Step 5 (UI Canvas) failed: {e.Message}\n{e.StackTrace}");
        }

        // ── 6. WindZone ───────────────────────────────────────────────────────
        try
        {
            var windZoneGO = ObjectFactory.CreateGameObject("WindZone",
                typeof(BoxCollider2D));
            windZoneGO.transform.position = Vector3.zero;

            var col = windZoneGO.GetComponent<BoxCollider2D>();
            if (col == null) throw new Exception("BoxCollider2D missing on WindZone.");
            col.isTrigger = true;
            col.size      = new Vector2(30f, 10f);

            var wc = windZoneGO.AddComponent<WindZoneController>();
            if (wc == null)
                Debug.LogWarning("[BuildGameScene] WindZoneController could not be added.");

            // DragonPosition – off-screen child empty, no sprite
            var dragonPos = ObjectFactory.CreateGameObject("DragonPosition");
            dragonPos.transform.SetParent(windZoneGO.transform, false);
            dragonPos.transform.position = new Vector3(25f, 15f, 0f); // well off-screen

            Debug.Log("[BuildGameScene] WindZone + DragonPosition created.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] Step 6 (WindZone) failed: {e.Message}\n{e.StackTrace}");
        }

        // ── 7. Enemy ──────────────────────────────────────────────────────────
        try
        {
            var enemyGO = ObjectFactory.CreateGameObject("Enemy",
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D));
            enemyGO.transform.position = new Vector3(5f, -3f, 0f);

            var sr = enemyGO.GetComponent<SpriteRenderer>();
            if (sr == null) throw new Exception("SpriteRenderer missing on Enemy.");
            sr.sprite = CreateCapsuleSprite(new Color(0.9f, 0.2f, 0.2f));

            var rb = enemyGO.GetComponent<Rigidbody2D>();
            if (rb == null) throw new Exception("Rigidbody2D missing on Enemy.");
            rb.bodyType = RigidbodyType2D.Kinematic;

            var ep = enemyGO.AddComponent<EnemyPatrol>();
            if (ep == null)
                Debug.LogWarning("[BuildGameScene] EnemyPatrol could not be added.");
            else
            {
                ep.leftBound  = 2f;
                ep.rightBound = 8f;
            }

            Debug.Log("[BuildGameScene] Enemy created.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] Step 7 (Enemy) failed: {e.Message}\n{e.StackTrace}");
        }

        // ── 8. Element Pickups ────────────────────────────────────────────────
        try
        {
            SpawnElementPickup("EarthPickup",  new Vector3(-8f, -3f, 0f), PlayerController.CreateEarthElement());
            SpawnElementPickup("FirePickup",   new Vector3( 0f, -3f, 0f), PlayerController.CreateFireElement());
            SpawnElementPickup("WaterPickup",  new Vector3( 8f, -3f, 0f), PlayerController.CreateWaterElement());
            Debug.Log("[BuildGameScene] Element pickups: Earth x=-8, Fire x=0, Water x=8.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] Step 8 (ElementPickups) failed: {e.Message}\n{e.StackTrace}");
        }

        // ── 9. Spike Hazards ──────────────────────────────────────────────────
        try
        {
            float[] spikeXs = { -12f, -6f, 3f, 9f, 14f };
            for (int i = 0; i < spikeXs.Length; i++)
                SpawnSpikeHazard($"Spike_{i}", new Vector3(spikeXs[i], -3.6f, 0f));
            Debug.Log("[BuildGameScene] 5 spike hazards placed.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] Step 9 (SpikeHazards) failed: {e.Message}\n{e.StackTrace}");
        }

        // ── 10. Ranged Enemy ──────────────────────────────────────────────────
        try
        {
            var rangedGO = ObjectFactory.CreateGameObject("RangedEnemy",
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D));
            rangedGO.transform.position = new Vector3(-6f, -3f, 0f);

            var rsr = rangedGO.GetComponent<SpriteRenderer>();
            if (rsr == null) throw new Exception("SpriteRenderer missing on RangedEnemy.");
            rsr.sprite = CreateCapsuleSprite(new Color(0.9f, 0.5f, 0.1f));

            var rrb = rangedGO.GetComponent<Rigidbody2D>();
            if (rrb == null) throw new Exception("Rigidbody2D missing on RangedEnemy.");
            rrb.bodyType = RigidbodyType2D.Kinematic;

            var ep = rangedGO.AddComponent<EnemyPatrol>();
            if (ep == null)
                Debug.LogWarning("[BuildGameScene] EnemyPatrol could not be added to RangedEnemy.");
            else
            {
                ep.leftBound     = -9f;
                ep.rightBound    = -3f;
                ep.shootInterval = 2f;   // fires faster than the default 3s
                ep.shootRange    = 12f;
                // projectilePrefab left null; EnemyPatrol.CreateRuntimeProjectile handles it
            }
            Debug.Log("[BuildGameScene] Ranged enemy at (-6,-3) with shootInterval=2.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] Step 10 (RangedEnemy) failed: {e.Message}\n{e.StackTrace}");
        }

        // ── Save scene ────────────────────────────────────────────────────────
        try
        {
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/PlatformerScene.unity");
            AssetDatabase.Refresh();
            Debug.Log("[BuildGameScene] Scene saved → Assets/Scenes/PlatformerScene.unity");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] Scene save failed: {e.Message}\n{e.StackTrace}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a 16×16 solid-colour Texture2D and wraps it in a Sprite.
    /// Used everywhere a placeholder art asset is needed.
    /// </summary>
    private static Sprite CreateSquareSprite(Color color)
    {
        var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var pixels = new Color[256];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
    }

    /// <summary>
    /// Creates a 16×24 ellipse sprite as a cheap capsule stand-in for enemies.
    /// </summary>
    private static Sprite CreateCapsuleSprite(Color color)
    {
        const int w = 16, h = 24;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        float cx = w * 0.5f, cy = h * 0.5f;
        float rx = w * 0.5f, ry = h * 0.5f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f - cx) / rx;
                float dy = (y + 0.5f - cy) / ry;
                tex.SetPixel(x, y, dx * dx + dy * dy <= 1f ? color : Color.clear);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    /// <summary>
    /// Saves a temporary Tile asset backed by a white square sprite.
    /// Returns null only if the AssetDatabase write fails.
    /// </summary>
    private static Tile CreateFallbackTile()
    {
        try
        {
            Directory.CreateDirectory("Assets/Tiles");
            var sprite = CreateSquareSprite(Color.white);
            var tile   = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            AssetDatabase.CreateAsset(tile, "Assets/Tiles/GroundTile.asset");
            AssetDatabase.SaveAssets();
            return tile;
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] CreateFallbackTile failed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates one parallax layer: a SpriteRenderer quad + ParallaxLayer component.
    /// </summary>
    private static void CreateParallaxLayer(string name, Vector3 position,
                                             Vector2 speed, Color color)
    {
        var go = ObjectFactory.CreateGameObject(name,
            typeof(SpriteRenderer), typeof(ParallaxLayer));
        go.transform.position   = position;
        go.transform.localScale = new Vector3(40f, 12f, 1f);

        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            Debug.LogError($"[BuildGameScene] SpriteRenderer missing on {name}.");
            return;
        }
        sr.sprite        = CreateSquareSprite(color);
        sr.sortingOrder  = -10;

        var pl = go.GetComponent<ParallaxLayer>();
        if (pl == null)
        {
            Debug.LogWarning($"[BuildGameScene] ParallaxLayer could not be added to {name}.");
            return;
        }
        pl.scrollSpeed = speed;
    }

    /// <summary>
    /// Builds the full UI hierarchy under a Screen-Space-Overlay Canvas.
    /// All element names match what UIManager.cs searches for at runtime.
    /// </summary>
    private static void BuildUICanvas()
    {
        // Canvas root
        var canvasGO = ObjectFactory.CreateGameObject("Canvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.GetComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;

        // UIManager lives on the Canvas – finds children by name at runtime.
        var uiMgr = canvasGO.AddComponent<UIManager>();
        if (uiMgr == null)
            Debug.LogWarning("[BuildGameScene] UIManager could not be added to Canvas.");

        // EventSystem (required for button clicks)
        if (UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            ObjectFactory.CreateGameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
        }

        Font builtinFont = GetBuiltinFont();

        // Helper: create a labeled Button child
        void AddButton(string btnName, Vector2 anchoredPos)
        {
            try
            {
                var btnGO = ObjectFactory.CreateGameObject(btnName,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                btnGO.transform.SetParent(canvasGO.transform, false);
                var rt = btnGO.GetComponent<RectTransform>();
                rt.sizeDelta       = new Vector2(180f, 44f);
                rt.anchoredPosition = anchoredPos;

                // Label
                var labelGO = ObjectFactory.CreateGameObject("Text",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                labelGO.transform.SetParent(btnGO.transform, false);
                var t = labelGO.GetComponent<Text>();
                if (t != null)
                {
                    t.text      = btnName;
                    t.font      = builtinFont;
                    t.alignment = TextAnchor.MiddleCenter;
                    t.color     = Color.white;
                    t.fontSize  = 16;
                }
                var lrt = labelGO.GetComponent<RectTransform>();
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.sizeDelta = Vector2.zero;
            }
            catch (Exception e)
            {
                Debug.LogError($"[BuildGameScene] Failed to create button '{btnName}': {e.Message}");
            }
        }

        AddButton("StartButton",   new Vector2(0f,  90f));
        AddButton("OptionsButton", new Vector2(0f,  40f));
        AddButton("QuitButton",    new Vector2(0f, -10f));

        // VolumeSlider
        try
        {
            var sliderGO = ObjectFactory.CreateGameObject("VolumeSlider",
                typeof(RectTransform), typeof(Slider));
            sliderGO.transform.SetParent(canvasGO.transform, false);
            var rt = sliderGO.GetComponent<RectTransform>();
            rt.sizeDelta        = new Vector2(220f, 30f);
            rt.anchoredPosition = new Vector2(0f, -65f);
            var slider = sliderGO.GetComponent<Slider>();
            if (slider != null) { slider.minValue = 0f; slider.maxValue = 1f; slider.value = 0.8f; }
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] VolumeSlider creation failed: {e.Message}");
        }

        // ScoreText – anchored top-right
        try
        {
            var scoreGO = ObjectFactory.CreateGameObject("ScoreText",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            scoreGO.transform.SetParent(canvasGO.transform, false);
            var rt = scoreGO.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(1f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-12f, -12f);
            rt.sizeDelta        = new Vector2(200f, 40f);
            var t = scoreGO.GetComponent<Text>();
            if (t != null)
            {
                t.text      = "Score: 0";
                t.font      = builtinFont;
                t.color     = Color.white;
                t.fontSize  = 18;
                t.alignment = TextAnchor.UpperRight;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] ScoreText creation failed: {e.Message}");
        }

        // OptionsPanel – hidden by default; toggled by OptionsButton via UIManager
        try
        {
            var panelGO = ObjectFactory.CreateGameObject("OptionsPanel",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelGO.transform.SetParent(canvasGO.transform, false);
            var rt = panelGO.GetComponent<RectTransform>();
            rt.sizeDelta        = new Vector2(300f, 200f);
            rt.anchoredPosition = Vector2.zero;
            panelGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
            panelGO.SetActive(false);
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] OptionsPanel creation failed: {e.Message}");
        }

        // ── Element / dash HUD elements ──────────────────────────────────────────────────
        // These names match what UIManager.cs searches for at runtime.

        // ElementDisplay – text + coloured image panel, bottom-left
        try
        {
            var elemGO = ObjectFactory.CreateGameObject("ElementDisplay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Text));
            elemGO.transform.SetParent(canvasGO.transform, false);
            var rt = elemGO.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 0f);
            rt.anchorMax        = new Vector2(0f, 0f);
            rt.pivot            = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(12f, 12f);
            rt.sizeDelta        = new Vector2(120f, 36f);
            elemGO.GetComponent<Image>().color = new Color(0.9f, 0.9f, 1f, 0.7f);
            var t = elemGO.GetComponent<Text>();
            if (t != null)
            {
                t.text      = "Air";
                t.font      = builtinFont;
                t.fontSize  = 16;
                t.color     = Color.white;
                t.alignment = TextAnchor.MiddleCenter;
            }
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] ElementDisplay failed: {e.Message}"); }

        // DashChargesDisplay – circle indicators
        try
        {
            var chargesGO = ObjectFactory.CreateGameObject("DashChargesDisplay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            chargesGO.transform.SetParent(canvasGO.transform, false);
            var rt = chargesGO.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 0f);
            rt.anchorMax        = new Vector2(0f, 0f);
            rt.pivot            = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(12f, 52f);
            rt.sizeDelta        = new Vector2(100f, 28f);
            var t = chargesGO.GetComponent<Text>();
            if (t != null)
            {
                t.text      = "\u25cf";
                t.font      = builtinFont;
                t.fontSize  = 18;
                t.color     = Color.white;
                t.alignment = TextAnchor.MiddleLeft;
            }
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] DashChargesDisplay failed: {e.Message}"); }

        // DashChargeMeter – horizontal slider that fills while holding LMB
        try
        {
            var meterGO = ObjectFactory.CreateGameObject("DashChargeMeter",
                typeof(RectTransform), typeof(Slider));
            meterGO.transform.SetParent(canvasGO.transform, false);
            var rt = meterGO.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 0f);
            rt.anchorMax        = new Vector2(0f, 0f);
            rt.pivot            = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(12f, 84f);
            rt.sizeDelta        = new Vector2(140f, 18f);
            var sl = meterGO.GetComponent<Slider>();
            if (sl != null) { sl.minValue = 0f; sl.maxValue = 1f; sl.value = 0f; }
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] DashChargeMeter failed: {e.Message}"); }
    }

    /// <summary>
    /// Tries to load the built-in Unity font.
    /// Unity 2022+: "LegacyRuntime.ttf"; older versions: "Arial.ttf".
    /// </summary>
    private static Font GetBuiltinFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null)
        {
            f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f == null)
                Debug.LogWarning("[BuildGameScene] Built-in font not found; Text labels will use Unity default.");
        }
        return f;
    }

    // ── Element pickup / spike / hazard spawn helpers ─────────────────────────────

    private static void SpawnElementPickup(string objName, Vector3 position, ElementStats element)
    {
        try
        {
            var go = ObjectFactory.CreateGameObject(objName,
                typeof(SpriteRenderer), typeof(CircleCollider2D));
            go.transform.position   = position;
            go.transform.localScale = Vector3.one * 0.5f;

            var cc = go.GetComponent<CircleCollider2D>();
            if (cc != null) cc.isTrigger = true;

            var ep = go.AddComponent<ElementPickup>();
            if (ep == null)
            {
                Debug.LogWarning($"[BuildGameScene] ElementPickup could not be added to '{objName}'. " +
                                 "Ensure ElementPickup.cs has compiled.");
                return;
            }
            ep.elementToGive = element;
            ep.respawnTime   = 5f;
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] SpawnElementPickup '{objName}' failed: {e.Message}");
        }
    }

    private static void SpawnSpikeHazard(string objName, Vector3 position)
    {
        try
        {
            var go = ObjectFactory.CreateGameObject(objName,
                typeof(SpriteRenderer), typeof(BoxCollider2D));
            go.transform.position   = position;
            go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);

            var col = go.GetComponent<BoxCollider2D>();
            if (col != null) col.isTrigger = true;

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sprite = CreateSquareSprite(new Color(0.7f, 0.7f, 0.8f));

            var sh = go.AddComponent<SpikeHazard>();
            if (sh == null)
                Debug.LogWarning($"[BuildGameScene] SpikeHazard could not be added to '{objName}'. " +
                                 "Ensure SpikeHazard.cs has compiled.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] SpawnSpikeHazard '{objName}' failed: {e.Message}");
        }
    }
}
