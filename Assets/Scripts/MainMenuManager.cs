using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

// The game's entry-point screen: title, Play/Level Guide/Settings/Exit buttons, and two
// popup panels (Level Guide, Settings), all built procedurally via GameUIHelper - same
// cozy-library backdrop, wood panels, fonts and colors as Levels 1-3, but this scene has
// no gameplay of its own to preserve. Kept deliberately minimal (a small 3-level
// educational game doesn't need a separate How To Play screen - the Level Guide covers
// everything a player needs).
public class MainMenuManager : MonoBehaviour
{
    private const string LevelSelectSceneName = "LevelSelect";
    private const string MusicPrefKey = "MusicOn";
    private const string SfxPrefKey = "SfxOn";

    // Matches the new Light Beige wall color (LibraryEnvironment.LightBeigeWall) so any
    // sliver of camera clear color peeking past the (generously oversized) wall sprite
    // blends in seamlessly instead of showing the old dark brown.
    private static readonly Color32 BrightLibraryBackground = new Color32(0xE8, 0xDD, 0xC8, 0xFF);

    // Used by the Settings popup: warm medium wood tone at 85% opacity so the library
    // backdrop still shows through slightly. Built via the Color32->Color implicit
    // conversion (which correctly normalizes the 0-255 byte channels to 0-1 floats) -
    // reading WoodBrown.r/.g/.b directly as Color32 bytes and feeding them straight into
    // `new Color(...)` would pass values like 109 into a 0-1 float channel, massively
    // overbright/clipped to white - that was the previous bug that made this panel render
    // white instead of wood.
    private static readonly Color PopupCardColor = MakeTranslucent(GameUIHelper.WoodBrown, 0.85f);

    // Used by the Level Guide popup only: fully opaque light cream with a thin gold
    // border frame, distinct from the translucent wood-panel look used elsewhere, per an
    // explicit request for that popup specifically.
    // Darkened from the original near-white 0xF8F3E8 and the text darkened from 0x4A3324
    // to near-black - the previous pairing read as low-contrast/washed-out on screen.
    private static readonly Color LevelGuideCardColor = new Color32(0xE8, 0xDA, 0xBE, 0xFF);
    private static readonly Color LevelGuideBorderColor = new Color32(0xD4, 0xAF, 0x37, 0xFF);
    private static readonly Color LevelGuideTextColor = new Color32(0x2B, 0x1A, 0x0E, 0xFF);

    private static Color MakeTranslucent(Color32 color, float alpha)
    {
        Color c = color;
        c.a = alpha;
        return c;
    }

    private GameObject mainMenuRoot;
    private GameObject levelGuideOverlay;
    private GameObject settingsOverlay;

    void Start()
    {
        if (Camera.main != null)
            Camera.main.backgroundColor = BrightLibraryBackground;

        BuildEnvironment();
        ApplyWarmLighting();
        BuildUI();
    }

    // Same cozy-library backdrop as the level scenes (wall/floor/bookshelf/window/plant),
    // minus the hanging lamp - kept as its own small implementation (mirroring
    // QuizManager.BuildEnvironment) rather than a shared helper, so no level scene ever
    // has to be touched for a menu-only change.
    void BuildEnvironment()
    {
        Camera cam = Camera.main;
        float frameCenterY = cam != null ? cam.transform.position.y : 0f;
        float frameHalfHeight = cam != null && cam.orthographic ? cam.orthographicSize : 5f;
        float aspect = cam != null ? cam.aspect : 1.6f;
        float frameHalfWidth = frameHalfHeight * aspect;

        float floorHeightEstimate = Mathf.Max(frameHalfHeight * 0.9f, 2f);
        float floorY = frameCenterY - frameHalfHeight + floorHeightEstimate * 0.75f;

        Vector3 groundAnchor = new Vector3(0f, floorY, 0f);
        Vector3 topAnchor = new Vector3(0f, frameCenterY + frameHalfHeight * 0.6f, 0f);

        // sharedMaterial: null - a freshly added SpriteRenderer with no material picks up
        // this project's URP 2D default-lit material automatically, so it still responds
        // correctly to the Global Light 2D (see QuizManager.BuildEnvironment for the
        // confirmation that this matches Level 1's book material exactly).
        LibraryEnvironment.Build(groundAnchor, topAnchor, frameCenterY, frameHalfHeight, frameHalfWidth, null,
            centerX: 0f, floorTopY: floorY, includeLamp: false);

        AdjustEnvironmentForMenu();
    }

    // Menu-only tweaks to the shared LibraryEnvironment output (find-by-name-and-adjust
    // after Build(), same pattern as QueueManager.SoftenLampPointLight) rather than
    // changing LibraryEnvironment.cs itself, which Levels 1-3 also rely on.
    void AdjustEnvironmentForMenu()
    {
        GameObject envRoot = GameObject.Find("LibraryEnvironment");
        if (envRoot == null) return;

        Transform bookshelf = envRoot.transform.Find("Bookshelf");
        if (bookshelf != null)
        {
            bookshelf.localPosition += new Vector3(-1f, 0f, 0f);
            bookshelf.localScale = Vector3.one * 1.15f;
        }

        Transform window = envRoot.transform.Find("Window");
        if (window != null)
            window.localPosition += new Vector3(-1f, 0f, 0f);

        Transform plant = envRoot.transform.Find("Plant");
        if (plant != null)
            plant.localScale *= 1.25f;
    }

    void ApplyWarmLighting()
    {
        Light2D light = Object.FindFirstObjectByType<Light2D>();
        if (light == null)
        {
            GameObject lightGO = new GameObject("Global Light 2D");
            light = lightGO.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
        }

        light.color = new Color(1f, 0.92f, 0.8f);
        light.intensity = 1.6f;
    }

    void BuildUI()
    {
        Canvas canvas = GameUIHelper.EnsureCanvas();

        // All the "main menu" visuals (title + buttons) live under one root so a popup
        // can hide the whole thing behind it with a single SetActive(false), instead of
        // just dimming/covering it.
        mainMenuRoot = new GameObject("MainMenuRoot", typeof(RectTransform));
        mainMenuRoot.transform.SetParent(canvas.transform, false);
        RectTransform rootRect = mainMenuRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.sizeDelta = Vector2.zero;
        rootRect.anchoredPosition = Vector2.zero;

        // Banner ~10% shorter (90 -> 81) and moved up slightly (-40 -> -34) to leave more
        // breathing room above the Play button; width unchanged.
        GameUIHelper.CreateWoodPanel(mainMenuRoot.transform, "TitleBanner", GameUIHelper.DarkWood,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(460, 81));

        GameUIHelper.CreateText(mainMenuRoot.transform, "TitleText", "SHELF ORDER", 38,
            GameUIHelper.GoldColor, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(460, 60));

        BuildMainButtons(mainMenuRoot.transform);

        levelGuideOverlay = BuildLevelGuidePanel(canvas.transform);
        settingsOverlay = BuildSettingsPanel(canvas.transform);
    }

    void BuildMainButtons(Transform parent)
    {
        // Button height is 56, so a 52 center-to-center spacing (the previous value) left
        // only a -4px gap - the buttons actually overlapped. 80 gives a real ~24px gap
        // between edges, and startY is raised to keep the 4-button block roughly balanced
        // between the title banner above and the bottom of the screen below.
        const float spacing = 80f;
        const float startY = 80f;
        Vector2 buttonSize = new Vector2(260, 56);
        Vector2 anchor = new Vector2(0.5f, 0.5f);

        GameUIHelper.CreateWoodButton(parent, "PlayButton", "Play", anchor, anchor,
            new Vector2(0, startY), buttonSize, OnPlay);
        GameUIHelper.CreateWoodButton(parent, "LevelGuideButton", "Level Guide", anchor, anchor,
            new Vector2(0, startY - spacing), buttonSize, OnLevelGuide);
        GameUIHelper.CreateWoodButton(parent, "SettingsButton", "Settings", anchor, anchor,
            new Vector2(0, startY - spacing * 2), buttonSize, OnSettings);
        GameUIHelper.CreateWoodButton(parent, "ExitButton", "Exit", anchor, anchor,
            new Vector2(0, startY - spacing * 3), buttonSize, OnExit);
    }

    void OnPlay() => SceneManager.LoadScene(LevelSelectSceneName);

    void OnLevelGuide()
    {
        mainMenuRoot.SetActive(false);
        levelGuideOverlay.SetActive(true);
    }

    void OnSettings()
    {
        mainMenuRoot.SetActive(false);
        settingsOverlay.SetActive(true);
    }

    // Quits the built application; in the Editor there is no process to quit, so this
    // stops Play Mode instead (Application.Quit() silently does nothing in-Editor).
    void OnExit()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Covers everything a player needs (mechanics + how each level maps to Stack/Queue),
    // so a separate How To Play screen would just be duplicate information. Opaque light
    // cream card with a real gold border frame (not the translucent wood card used
    // elsewhere) - the dim full-screen overlay behind it (built into BuildOverlayCard)
    // is what dims the background; the card itself no longer shows anything through it.
    // Taller than before so title/sections/button all get proper breathing room instead
    // of being squeezed into a shorter box.
    GameObject BuildLevelGuidePanel(Transform canvasTransform)
    {
        GameObject overlay = GameUIHelper.BuildOverlayCard(canvasTransform, "LevelGuidePanel",
            new Vector2(600, 400), out Transform card, LevelGuideCardColor,
            useWoodTexture: false, borderColorOverride: LevelGuideBorderColor);

        GameUIHelper.CreateText(card, "Title", "Level Guide", 26, GameUIHelper.GoldColor, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(560, 30));

        // Sections separated by a full blank line (consistent, equal gap between all
        // three), rather than the previous cramped negative line spacing.
        string body =
            "Level 1 - Stack (LIFO)\n" +
            "• Remove books from the top of the stack to reveal the hidden library key.\n" +
            "• Demonstrates the Last In, First Out (LIFO) principle.\n\n" +
            "Level 2 - Queue (FIFO)\n" +
            "• Serve library visitors in the order they arrived.\n" +
            "• Demonstrates the First In, First Out (FIFO) principle.\n\n" +
            "Level 3 - Stack or Queue?\n" +
            "• Read each library scenario.\n" +
            "• Decide whether it represents a Stack or a Queue.";

        TextMeshProUGUI bodyText = GameUIHelper.CreateText(card, "Body", body, 18, LevelGuideTextColor,
            FontStyles.Normal, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -191), new Vector2(560, 260));
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.enableAutoSizing = true;
        bodyText.fontSizeMin = 14;
        bodyText.fontSizeMax = 18;

        GameUIHelper.CreateWoodButton(card, "BackButton", "Back",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(150, 40),
            () =>
            {
                overlay.SetActive(false);
                mainMenuRoot.SetActive(true);
            });

        return overlay;
    }

    // Music/SFX are stored via PlayerPrefs so a future AudioSource-based sound system can
    // read them - there is no persistent music/SFX player in the project yet, so these
    // toggles now go through AudioManager (a persistent, DontDestroyOnLoad singleton)
    // so Music/SFX take effect immediately instead of only being read from PlayerPrefs
    // the next time some other code happens to check it.
    GameObject BuildSettingsPanel(Transform canvasTransform)
    {
        GameObject overlay = GameUIHelper.BuildOverlayCard(canvasTransform, "SettingsPanel",
            new Vector2(440, 210), out Transform card, PopupCardColor);

        GameUIHelper.CreateText(card, "Title", "Settings", 26, GameUIHelper.GoldColor, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(380, 30));

        bool musicOn = PlayerPrefs.GetInt(MusicPrefKey, 1) == 1;
        bool sfxOn = PlayerPrefs.GetInt(SfxPrefKey, 1) == 1;

        GameUIHelper.CreateWoodToggle(card, "MusicToggle", "Music", musicOn,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-90, 26), new Vector2(32, 32),
            isOn => AudioManager.SetMusicEnabled(isOn));

        GameUIHelper.CreateWoodToggle(card, "SfxToggle", "Sound Effects", sfxOn,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-90, -16), new Vector2(32, 32),
            isOn => AudioManager.SetSfxEnabled(isOn));

        GameUIHelper.CreateWoodButton(card, "BackButton", "Back",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(150, 42),
            () =>
            {
                overlay.SetActive(false);
                mainMenuRoot.SetActive(true);
            });

        return overlay;
    }
}
