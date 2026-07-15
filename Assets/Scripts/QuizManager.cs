using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using TMPro;

public class QuizManager : MonoBehaviour
{
    public TextMeshProUGUI questionText;
    public TextMeshProUGUI feedbackText;

    public AudioClip correctAnswerSound; // optional - assign a free SFX clip; safely no-ops if left empty
    public AudioClip wrongAnswerSound;
    public AudioClip quizCompleteSound;

    private TextMeshProUGUI progressText;
    private GameObject feedbackPanel;
    private Image feedbackPanelImage;
    private bool answering = true;

    // Quiz Complete popup - same cream/gold-border/rounded/shadow card and entrance
    // animation as Levels 1/2's completion popups (StackManager/QueueManager).
    private GameObject levelCompletePanel;
    private TextMeshProUGUI scoreNumberText;
    private TextMeshProUGUI messageText;
    private Image popupOverlayImage;
    private CanvasGroup popupCardGroup;
    private CanvasGroup popupTextGroup;
    private CanvasGroup popupButtonGroup;
    private float popupOverlayTargetAlpha;

    private static readonly Color32 CorrectColor = new Color32(0x2E, 0xCC, 0x71, 0x80);
    private static readonly Color32 WrongColor = new Color32(0xE7, 0x4C, 0x3C, 0x80);

    // Matches the new Light Beige wall color (LibraryEnvironment.LightBeigeWall) so any
    // sliver of camera clear color peeking past the (generously oversized) wall sprite
    // blends in seamlessly instead of showing the old dark brown.
    private static readonly Color32 BrightLibraryBackground = new Color32(0xE8, 0xDD, 0xC8, 0xFF);

    private string[] questions = {
        "People waiting in line at a library",
        "Undo button in a text editor",
        "Printer printing documents in order",
        "Browser back button history",
        "Plates stacked in a cupboard",
        "Customers in a bank teller line",
        "Function calls during recursion",
        "Cars waiting at a toll booth",
        "Pancakes stacked on a plate",
        "Support tickets handled in the order received",
        "Reversing a word using push and pop operations",
        "CPU processes waiting in a ready queue",
        "A stack of trays in a cafeteria",
        "Checking if brackets in an expression are balanced",
        "Messages processed in the order they arrive in a chat"
    };

    private string[] answers = {
        "Queue",
        "Stack",
        "Queue",
        "Stack",
        "Stack",
        "Queue",
        "Stack",
        "Queue",
        "Stack",
        "Queue",
        "Stack",
        "Queue",
        "Stack",
        "Stack",
        "Queue"
    };

    private int currentQuestion = 0;
    private int score = 0;

    // questions/answers stay in their authored (currently alternating Queue/Stack) order -
    // this holds a shuffled permutation of their indices instead, so the on-screen order is
    // randomized per playthrough without touching the question/answer arrays themselves.
    private int[] questionOrder;

    void Start()
    {
        if (Camera.main != null)
            Camera.main.backgroundColor = BrightLibraryBackground;

        questionOrder = ShuffledIndices(questions.Length);

        BuildEnvironment();
        ApplyWarmLighting();

        BuildUI();

        if (feedbackText != null) feedbackText.gameObject.SetActive(false);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);

        ShowQuestion();
    }

    // Replaces the old flat purple background with the same procedural library backdrop
    // used by Levels 1/2 (wall/floor/bookshelf/window/plant), minus the hanging lamp -
    // this screen has no world-space content to serve as a "table/stack" anchor, so the
    // camera's own existing frame (already sized to show the full quiz UI) is reused as
    // the reference instead of computing one from books/visitors.
    void BuildEnvironment()
    {
        // The old scene-authored dark translucent overlay was there to dim the flat purple
        // background - it would now just tint the new environment unevenly, working against
        // the "evenly lit, no dark vignette" requirement, so it's no longer needed.
        GameObject oldOverlay = GameObject.Find("Backkground");
        if (oldOverlay != null) oldOverlay.SetActive(false);

        Camera cam = Camera.main;
        float frameCenterY = cam != null ? cam.transform.position.y : 0f;
        float frameHalfHeight = cam != null && cam.orthographic ? cam.orthographicSize : 5f;
        float aspect = cam != null ? cam.aspect : 1.6f;
        float frameHalfWidth = frameHalfHeight * aspect;

        // Same floor-top formula LibraryEnvironment uses internally by default - computed
        // explicitly here so the decorative door can be pinned to the exact same line the
        // floor graphic ends up on, instead of guessing a separate offset.
        float floorHeightEstimate = Mathf.Max(frameHalfHeight * 0.9f, 2f);
        float floorY = frameCenterY - frameHalfHeight + floorHeightEstimate * 0.75f;

        Vector3 groundAnchor = new Vector3(0f, floorY, 0f);
        Vector3 topAnchor = new Vector3(0f, frameCenterY + frameHalfHeight * 0.6f, 0f);

        // sharedMaterial: null - a freshly added SpriteRenderer with no material set is
        // auto-assigned this project's URP 2D default-lit material (the same one Level 1's
        // books use), so the environment still responds to the Global Light 2D correctly
        // without needing to hunt down a shared material the way Levels 1/2 do from an
        // existing book/visitor sprite.
        LibraryEnvironment.Build(groundAnchor, topAnchor, frameCenterY, frameHalfHeight, frameHalfWidth, null,
            centerX: 0f, floorTopY: floorY, includeLamp: false);

        CreateExitDoor(new Vector3(-frameHalfWidth * 0.55f, floorY + 1.2f, 2f));
    }

    // Purely decorative door (mirrors StackManager/QueueManager's) - no unlock/click logic,
    // just wall dressing, kept as its own small implementation here rather than a shared
    // helper so the other levels never have to be touched.
    void CreateExitDoor(Vector3 position)
    {
        GameObject door = new GameObject("ExitDoor");
        door.transform.position = position;

        SpriteRenderer doorSR = door.AddComponent<SpriteRenderer>();
        doorSR.sprite = GameUIHelper.GetRoundedWoodSprite(6);
        doorSR.drawMode = SpriteDrawMode.Sliced;
        doorSR.size = new Vector2(1.2f, 2.4f);
        doorSR.color = new Color(0.32f, 0.20f, 0.12f);

        GameObject handle = new GameObject("DoorHandle");
        handle.transform.SetParent(door.transform, false);
        handle.transform.localPosition = new Vector3(0.4f, 0f, -0.02f);

        SpriteRenderer handleSR = handle.AddComponent<SpriteRenderer>();
        handleSR.sprite = GameUIHelper.GetBlobSprite();
        handleSR.drawMode = SpriteDrawMode.Sliced;
        handleSR.size = new Vector2(0.14f, 0.14f);
        handleSR.color = GameUIHelper.GoldColor;
    }

    // Matches Level 2's lighting fix: creates the Global Light 2D if the scene doesn't
    // already have one (this scene never had any lighting setup at all), instead of a
    // Point Light/hanging lamp, so the room is evenly lit with no hotspot or vignette.
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

        BuildTitle(canvas.transform);
        BuildProgressPanel(canvas.transform);
        BuildQuestionPanel();
        RestyleAnswerButtons();
        BuildFeedbackPanel();
        BuildQuizCompletePopup(canvas.transform);
    }

    // Same cream card / gold border / rounded corners / soft shadow / fade+scale entrance
    // as Levels 1/2's completion popups (StackManager.BuildLevelCompletePopup /
    // QueueManager.BuildLevelCompletePopup), compact (280x240) with a horizontal button
    // row instead of stacked buttons. Built once here (hidden) since the card shell and
    // buttons are static; score/message are filled in later, once the final score is
    // known, by ShowQuizCompletePopup.
    void BuildQuizCompletePopup(Transform canvasTransform)
    {
        Color creamCard = new Color32(0xF7, 0xF1, 0xE3, 0xFF);
        Color goldBorder = new Color32(0xD4, 0xAF, 0x37, 0xFF);
        Color darkBrownText = new Color32(0x2B, 0x1A, 0x0E, 0xFF);

        GameObject overlay = GameUIHelper.BuildOverlayCard(canvasTransform, "QuizCompleteOverlay",
            new Vector2(280, 240), out Transform card, creamCard, useWoodTexture: false, borderColorOverride: goldBorder);

        popupOverlayImage = overlay.GetComponent<Image>();
        popupOverlayTargetAlpha = 0.22f;
        Color overlayColor = popupOverlayImage.color;
        overlayColor.a = popupOverlayTargetAlpha;
        popupOverlayImage.color = overlayColor;

        Shadow shadow = card.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(0, -6);

        popupCardGroup = card.gameObject.AddComponent<CanvasGroup>();

        GameObject textGroupGO = new GameObject("TextGroup", typeof(RectTransform));
        textGroupGO.transform.SetParent(card, false);
        RectTransform textGroupRect = textGroupGO.GetComponent<RectTransform>();
        textGroupRect.anchorMin = Vector2.zero;
        textGroupRect.anchorMax = Vector2.one;
        textGroupRect.sizeDelta = Vector2.zero;
        textGroupRect.anchoredPosition = Vector2.zero;
        popupTextGroup = textGroupGO.AddComponent<CanvasGroup>();

        // Title: largest text on the popup, centered at the top.
        GameUIHelper.CreateText(textGroupGO.transform, "Title", "Quiz Complete!", 22,
            darkBrownText, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(240, 26));

        // Score: small label, then a larger bold number below it.
        GameUIHelper.CreateText(textGroupGO.transform, "ScoreLabel", "Score", 12,
            darkBrownText, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -58), new Vector2(240, 16));

        scoreNumberText = GameUIHelper.CreateText(textGroupGO.transform, "ScoreNumber", "", 26,
            darkBrownText, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -90), new Vector2(240, 30));

        // Single performance message (replaces the old headline+body pair and the star
        // rating) - centered below the score, smaller font than the title, bold for
        // contrast against the cream card.
        messageText = GameUIHelper.CreateText(textGroupGO.transform, "Message", "", 14,
            darkBrownText, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -138), new Vector2(240, 44));
        messageText.enableAutoSizing = true;
        messageText.fontSizeMin = 11;
        messageText.fontSizeMax = 14;

        // Buttons: side by side (not stacked, unlike Levels 1/2), smaller than a
        // standard popup button, centered as a pair with a comfortable gap.
        GameObject buttonGroupGO = new GameObject("ButtonGroup", typeof(RectTransform));
        buttonGroupGO.transform.SetParent(card, false);
        RectTransform buttonGroupRect = buttonGroupGO.GetComponent<RectTransform>();
        buttonGroupRect.anchorMin = Vector2.zero;
        buttonGroupRect.anchorMax = Vector2.one;
        buttonGroupRect.sizeDelta = Vector2.zero;
        buttonGroupRect.anchoredPosition = Vector2.zero;
        popupButtonGroup = buttonGroupGO.AddComponent<CanvasGroup>();

        Vector2 buttonSize = new Vector2(120, 40);
        const float buttonY = -196;
        const float buttonGap = 10f;
        float buttonOffsetX = buttonSize.x * 0.5f + buttonGap * 0.5f;

        GameUIHelper.CreateWoodButton(buttonGroupGO.transform, "ReplayButton", "Replay",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-buttonOffsetX, buttonY), buttonSize,
            () => SceneManager.LoadScene(SceneManager.GetActiveScene().name));

        GameUIHelper.CreateWoodButton(buttonGroupGO.transform, "QuitButton", "Quit",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(buttonOffsetX, buttonY), buttonSize,
            () => SceneManager.LoadScene("MainMenu"));

        overlay.SetActive(false);
        levelCompletePanel = overlay;
    }

    // Fills in the final score/message, then plays the same entrance animation as
    // Levels 1/2 (card fades/scales in over ~0.3s alongside the overlay dimming in, then
    // the text group fades in, then the button group fades in).
    void ShowQuizCompletePopup()
    {
        if (scoreNumberText != null) scoreNumberText.text = score + " / " + questions.Length;

        // Message tiers are expressed as a percentage of questions.Length (not a hardcoded
        // score out of 10) so they keep working correctly no matter how many questions the
        // quiz has - the boundaries (90/70/50%) match the requested 9-10/7-8/5-6/0-4 out of 10.
        float percent = questions.Length > 0 ? (float)score / questions.Length : 0f;
        string message;
        if (percent >= 0.9f) message = "Excellent! You have mastered the Stack and Queue concepts.";
        else if (percent >= 0.7f) message = "Great Job! You have a good understanding of Stack and Queue.";
        else if (percent >= 0.5f) message = "Good Effort! Review the concepts and try again to improve.";
        else message = "Keep Practicing! Replay the quiz to strengthen your understanding.";

        if (messageText != null) messageText.text = message;

        StartCoroutine(AnimatePopupReveal());
    }

    private IEnumerator AnimatePopupReveal()
    {
        if (levelCompletePanel == null) yield break;
        levelCompletePanel.SetActive(true);

        if (popupTextGroup != null) popupTextGroup.alpha = 0f;
        if (popupButtonGroup != null) popupButtonGroup.alpha = 0f;
        if (popupCardGroup != null) popupCardGroup.alpha = 0f;
        if (popupCardGroup != null) popupCardGroup.transform.localScale = Vector3.one * 0.9f;

        Color startOverlay = popupOverlayImage.color;
        startOverlay.a = 0f;
        popupOverlayImage.color = startOverlay;

        const float duration = 0.3f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            if (popupCardGroup != null)
            {
                popupCardGroup.alpha = p;
                popupCardGroup.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, p);
            }
            Color c = popupOverlayImage.color;
            c.a = Mathf.Lerp(0f, popupOverlayTargetAlpha, p);
            popupOverlayImage.color = c;
            yield return null;
        }
        if (popupCardGroup != null)
        {
            popupCardGroup.alpha = 1f;
            popupCardGroup.transform.localScale = Vector3.one;
        }
        Color finalOverlay = popupOverlayImage.color;
        finalOverlay.a = popupOverlayTargetAlpha;
        popupOverlayImage.color = finalOverlay;

        yield return StartCoroutine(FadeGroup(popupTextGroup, 0.2f));
        yield return StartCoroutine(FadeGroup(popupButtonGroup, 0.2f));
    }

    private IEnumerator FadeGroup(CanvasGroup group, float duration)
    {
        if (group == null) yield break;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            group.alpha = Mathf.Clamp01(t / duration);
            yield return null;
        }
        group.alpha = 1f;
    }

    // Same wood banner treatment as Levels 1/2's header, built fresh via the shared
    // GameUIHelper methods - the old scene-authored "TitleText" (a plain floating white
    // label with no panel) is superseded by this and hidden.
    void BuildTitle(Transform canvasTransform)
    {
        GameUIHelper.CreateWoodPanel(canvasTransform, "HeaderBanner", GameUIHelper.DarkWood,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(400, 70));

        GameUIHelper.CreateText(canvasTransform, "HeaderText", "LEVEL 3 – STACK OR QUEUE?", 22,
            GameUIHelper.GoldColor, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -12), new Vector2(400, 44));

        GameObject oldTitle = GameObject.Find("TitleText");
        if (oldTitle != null) oldTitle.SetActive(false);
    }

    // Mirrors StackManager.BuildCounter/QueueManager.BuildCounter exactly - same wood
    // panel group, label line, and large value line, moved to the top-right corner.
    void BuildProgressPanel(Transform canvasTransform)
    {
        GameObject group = new GameObject("CounterGroup", typeof(RectTransform));
        group.transform.SetParent(canvasTransform, false);
        RectTransform groupRect = group.GetComponent<RectTransform>();
        groupRect.anchorMin = new Vector2(1f, 1f);
        groupRect.anchorMax = new Vector2(1f, 1f);
        groupRect.pivot = new Vector2(1f, 1f);
        groupRect.anchoredPosition = new Vector2(-24, -24);
        groupRect.sizeDelta = new Vector2(170, 64);

        GameUIHelper.CreateWoodPanel(group.transform, "CounterBackground", GameUIHelper.DarkWood,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 14);

        GameUIHelper.CreateText(group.transform, "CounterLabel", "Question", 13,
            GameUIHelper.Cream, FontStyles.Normal,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(150, 20));

        progressText = GameUIHelper.CreateText(group.transform, "CounterValue", "", 24,
            GameUIHelper.GoldColor, FontStyles.Bold,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 16), new Vector2(100, 28));
    }

    // Wraps the existing (scene-wired) questionText in a light cream rounded panel with a
    // soft drop shadow, instead of floating text - inserted at questionText's own sibling
    // index so it renders behind it, same recipe already used below for the feedback panel.
    void BuildQuestionPanel()
    {
        if (questionText == null) return;

        RectTransform textRect = questionText.GetComponent<RectTransform>();

        // Scaled down from the scene-authored 700x100 box so the whole question board
        // reads a bit smaller/tighter, not just the panel padding around it.
        textRect.sizeDelta = new Vector2(580, 90);

        GameObject panelGO = new GameObject("QuestionPanel", typeof(RectTransform));
        panelGO.transform.SetParent(textRect.parent, false);

        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = textRect.anchorMin;
        panelRect.anchorMax = textRect.anchorMax;
        panelRect.anchoredPosition = textRect.anchoredPosition;
        panelRect.sizeDelta = textRect.sizeDelta + new Vector2(50, 34);

        Image panelImage = panelGO.AddComponent<Image>();
        panelImage.sprite = GameUIHelper.GetRoundedSprite(20);
        panelImage.type = Image.Type.Sliced;
        panelImage.color = GameUIHelper.Cream;

        Shadow shadow = panelGO.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(3, -3);

        panelGO.transform.SetSiblingIndex(questionText.transform.GetSiblingIndex());

        // White text on a light cream panel would be unreadable - dark wood matches the
        // rest of the project's text-on-light-surface convention.
        questionText.color = GameUIHelper.DarkWood;
        questionText.alignment = TextAlignmentOptions.Center;
    }

    // Restyles the existing StackButton/QueueButton in place - only their Image (sprite/
    // color) and label text color change; the Button components and their scene-wired
    // OnClick -> AnswerStack/AnswerQueue calls are never touched, so functionality (and
    // the built-in hover/press color-tint transition already configured on them) is
    // completely unaffected.
    void RestyleAnswerButtons()
    {
        RestyleButton("StackButton");
        RestyleButton("QueueButton");
    }

    void RestyleButton(string name)
    {
        GameObject buttonGO = GameObject.Find(name);
        if (buttonGO == null) return;

        Image image = buttonGO.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = GameUIHelper.GetRoundedWoodSprite(16);
            image.type = Image.Type.Sliced;
            image.color = GameUIHelper.WoodBrown;
        }

        TextMeshProUGUI label = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.color = GameUIHelper.Cream;
            label.fontStyle = FontStyles.Bold;
        }

        // These Stack/Queue buttons are scene-authored (their OnClick -> AnswerStack/
        // AnswerQueue wiring lives in the scene file), so unlike buttons built via
        // GameUIHelper.CreateWoodButton/CreateFlatButton they don't get a click sound
        // automatically - add it here without touching their existing OnClick wiring.
        Button button = buttonGO.GetComponent<Button>();
        if (button != null) button.onClick.AddListener(AudioManager.PlayButtonClick);
    }

    // Unchanged behavior from before (a panel sized around feedbackText, inserted behind
    // it) - only pulled out into its own method since progressText now has its own home
    // in BuildProgressPanel instead of being created alongside this.
    void BuildFeedbackPanel()
    {
        if (feedbackText == null) return;

        Transform parent = feedbackText.transform.parent;
        RectTransform feedbackRect = feedbackText.GetComponent<RectTransform>();

        feedbackPanel = new GameObject("FeedbackPanel", typeof(RectTransform));
        feedbackPanel.transform.SetParent(parent, false);

        RectTransform panelRect = feedbackPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = feedbackRect.anchorMin;
        panelRect.anchorMax = feedbackRect.anchorMax;
        panelRect.anchoredPosition = feedbackRect.anchoredPosition;
        panelRect.sizeDelta = new Vector2(feedbackRect.sizeDelta.x + 60, feedbackRect.sizeDelta.y + 30);

        feedbackPanelImage = feedbackPanel.AddComponent<Image>();
        feedbackPanel.transform.SetSiblingIndex(feedbackText.transform.GetSiblingIndex());

        // The scene-authored text was left/top aligned (fine for a single short line, but
        // reads off-center once the panel got wider than the text) - center it both ways
        // so "Correct!"/"Wrong! Answer was: ..." sits centered inside the popup.
        feedbackText.alignment = TextAlignmentOptions.Center;

        // The panel background is now semi-transparent (see CorrectColor/WrongColor), so a
        // plain white fill no longer guarantees contrast against whatever's behind it - a
        // dark outline keeps the text readable regardless of what shows through.
        Outline textOutline = feedbackText.gameObject.AddComponent<Outline>();
        textOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        textOutline.effectDistance = new Vector2(1.5f, -1.5f);
    }

    // Fisher-Yates shuffle - returns a random permutation of 0..count-1 so questions play
    // in a different order each run instead of the authored (visibly alternating
    // Queue/Stack/Queue/Stack...) array order, which let players guess the answer pattern
    // without reading the question.
    private int[] ShuffledIndices(int count)
    {
        int[] order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    void ShowQuestion()
    {
        if (currentQuestion < questions.Length)
        {
            questionText.text = questions[questionOrder[currentQuestion]];
            if (progressText != null)
                progressText.text = (currentQuestion + 1) + " / " + questions.Length;
        }
    }

    public void AnswerStack()
    {
        if (!answering) return;
        CheckAnswer("Stack");
    }

    public void AnswerQueue()
    {
        if (!answering) return;
        CheckAnswer("Queue");
    }

    void CheckAnswer(string answer)
    {
        if (currentQuestion >= questions.Length) return; // stop if quiz done

        answering = false;
        feedbackText.gameObject.SetActive(true);
        if (feedbackPanel != null) feedbackPanel.SetActive(true);
        feedbackText.color = Color.white;

        if (answer == answers[questionOrder[currentQuestion]])
        {
            score++;
            feedbackText.text = "Correct!";
            if (feedbackPanelImage != null) feedbackPanelImage.color = CorrectColor;
            AudioManager.PlaySfx(correctAnswerSound, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        }
        else
        {
            feedbackText.text = "Wrong! Answer was: " + answers[questionOrder[currentQuestion]];
            if (feedbackPanelImage != null) feedbackPanelImage.color = WrongColor;
            AudioManager.PlaySfx(wrongAnswerSound, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        }

        currentQuestion++;

        if (currentQuestion >= questions.Length)
        {
            if (progressText != null) progressText.text = "";
            Debug.Log("Quiz done! Score: " + score);
            // Same short delay as Levels 1/2 before their completion popup - lets the
            // player see the final Correct!/Wrong! feedback before it's replaced.
            Invoke(nameof(FinishQuiz), 1f);
        }
        else
        {
            Invoke(nameof(ShowNextQuestion), 1.5f);
        }
    }

    void FinishQuiz()
    {
        feedbackText.gameObject.SetActive(false);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        AudioManager.PlaySfx(quizCompleteSound, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        ShowQuizCompletePopup();
    }

    void ShowNextQuestion()
    {
        feedbackText.gameObject.SetActive(false);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        answering = true;
        ShowQuestion();
    }
}
