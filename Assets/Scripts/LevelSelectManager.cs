using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using TMPro;

// Level Select screen: buttons for Level 1/2/3 plus a Back button to the Main Menu, in
// the same wood-panel/library-backdrop style as the rest of the game. Loads scenes by
// name only - it has no gameplay of its own and never touches a level's own logic.
public class LevelSelectManager : MonoBehaviour
{
    private const string MainMenuSceneName = "MainMenu";
    private const string Level1SceneName = "Level1_Stack";
    private const string Level2SceneName = "Level2_Queue";
    private const string Level3SceneName = "Level3_Quiz";

    // Matches the new Light Beige wall color (LibraryEnvironment.LightBeigeWall) so any
    // sliver of camera clear color peeking past the (generously oversized) wall sprite
    // blends in seamlessly instead of showing the old dark brown.
    private static readonly Color32 BrightLibraryBackground = new Color32(0xE8, 0xDD, 0xC8, 0xFF);

    void Start()
    {
        if (Camera.main != null)
            Camera.main.backgroundColor = BrightLibraryBackground;

        BuildEnvironment();
        ApplyWarmLighting();
        BuildUI();
    }

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

        LibraryEnvironment.Build(groundAnchor, topAnchor, frameCenterY, frameHalfHeight, frameHalfWidth, null,
            centerX: 0f, floorTopY: floorY, includeLamp: false);
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

        GameUIHelper.CreateWoodPanel(canvas.transform, "TitleBanner", GameUIHelper.DarkWood,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(400, 70));

        GameUIHelper.CreateText(canvas.transform, "TitleText", "SELECT A LEVEL", 22,
            GameUIHelper.GoldColor, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -12), new Vector2(400, 44));

        Vector2 buttonSize = new Vector2(220, 64);
        Vector2 anchor = new Vector2(0.5f, 0.5f);
        const float spacing = 76f;

        GameUIHelper.CreateWoodButton(canvas.transform, "Level1Button", "Level 1 - Stack", anchor, anchor,
            new Vector2(0, spacing), buttonSize, () => SceneManager.LoadScene(Level1SceneName));

        GameUIHelper.CreateWoodButton(canvas.transform, "Level2Button", "Level 2 - Queue", anchor, anchor,
            new Vector2(0, 0), buttonSize, () => SceneManager.LoadScene(Level2SceneName));

        GameUIHelper.CreateWoodButton(canvas.transform, "Level3Button", "Level 3 - Quiz", anchor, anchor,
            new Vector2(0, -spacing), buttonSize, () => SceneManager.LoadScene(Level3SceneName));

        GameUIHelper.CreateWoodButton(canvas.transform, "BackButton", "Back", anchor, anchor,
            new Vector2(0, -spacing * 2 - 20), new Vector2(160, 48),
            () => SceneManager.LoadScene(MainMenuSceneName));
    }
}
