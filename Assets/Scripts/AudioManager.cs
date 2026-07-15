using UnityEngine;

// Persistent (DontDestroyOnLoad) singleton that owns real-time Music/SFX mute state,
// backed by the same PlayerPrefs keys the Settings panel already writes to
// (MainMenuManager's "MusicOn"/"SfxOn"), so toggling a setting takes effect immediately
// instead of only being read the next time a clip happens to play.
//
// There are two ways an instance can come to exist, and the order matters:
//  1. Scene-placed: an "AudioManager" GameObject with this script (and its music/click
//     clips assigned in the Inspector) sits in MainMenu.unity, the game's entry point.
//  2. Code fallback (Bootstrap, below): if no instance exists yet - e.g. someone presses
//     Play directly on Level1/2/3 or Level Select in the Editor, skipping the Main Menu -
//     a bare instance is created with no clips assigned, so mute toggles and any
//     per-script SFX (like StackManager.pageTurnSound) still work correctly.
// Bootstrap runs AfterSceneLoad (not BeforeSceneLoad): BeforeSceneLoad fires before the
// very first scene's own objects have even Awoken, so it would always win the race
// against a scene-placed instance and get created first - then the *real* scene-placed
// one (with actual assigned clips) would see Instance already set and destroy itself,
// silently discarding those clips. AfterSceneLoad lets a scene-placed instance's own
// Awake() claim Instance first, so Bootstrap correctly finds it and no-ops.
public class AudioManager : MonoBehaviour
{
    private const string MusicPrefKey = "MusicOn";
    private const string SfxPrefKey = "SfxOn";

    public static AudioManager Instance { get; private set; }

    [Header("Background Music")]
    [Tooltip("Assign the Main Menu's background music track here. Plays automatically " +
        "when the Main Menu loads and keeps playing across scene changes.")]
    public AudioClip backgroundMusic;
    [Range(0f, 1f)]
    public float musicVolume = 0.6f;

    [Header("Global Sound Effects")]
    [Tooltip("Played whenever any wood/flat button built via GameUIHelper is clicked.")]
    public AudioClip buttonClickSound;

    private AudioSource musicSource;
    private AudioSource sfxSource;
    private bool musicEnabled = true;
    private bool sfxEnabled = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        // In case a scene-placed instance exists but hasn't been searched for yet
        // (AfterSceneLoad already guarantees Awake has run, but this is a harmless
        // extra safety check against creating a redundant second one).
        AudioManager existing = Object.FindFirstObjectByType<AudioManager>();
        if (existing != null) return;

        GameObject go = new GameObject("AudioManager");
        go.AddComponent<AudioManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        musicSource = GetComponent<AudioSource>();
        if (musicSource == null) musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = true;
        // AudioSource.spatialBlend defaults to 1 (fully 3D) when a component is freshly
        // added - never explicitly set before, meaning background music would have been
        // spatialized/affected by listener position instead of playing evenly. 0 = 2D.
        musicSource.spatialBlend = 0f;
        musicSource.volume = musicVolume;

        // Separate AudioSource for one-shot SFX, also on this DontDestroyOnLoad object.
        // Previously PlaySfx used AudioSource.PlayClipAtPoint, which spawns a temporary
        // GameObject *in the current scene* to play the clip and destroys it afterwards -
        // fine for sounds that happen mid-scene, but any button that also triggers
        // SceneManager.LoadScene() (Play, Level Select, etc.) unloads that scene almost
        // immediately, destroying the temp object and cutting the click sound off before
        // it's audible. Playing through a persistent source via PlayOneShot survives
        // scene changes.
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;

        musicEnabled = PlayerPrefs.GetInt(MusicPrefKey, 1) == 1;
        sfxEnabled = PlayerPrefs.GetInt(SfxPrefKey, 1) == 1;
        musicSource.mute = !musicEnabled;

        if (backgroundMusic != null)
        {
            musicSource.clip = backgroundMusic;
            musicSource.Play();
        }
    }

    // Called by the Settings panel's Music toggle - mutes/unmutes immediately, no scene
    // reload or clip re-assignment needed.
    public static void SetMusicEnabled(bool enabled)
    {
        if (Instance == null) return;
        Instance.musicEnabled = enabled;
        PlayerPrefs.SetInt(MusicPrefKey, enabled ? 1 : 0);
        if (Instance.musicSource != null) Instance.musicSource.mute = !enabled;
    }

    // Called by the Settings panel's Sound Effects toggle - future PlaySfx calls
    // immediately start/stop respecting it, no scene reload needed.
    public static void SetSfxEnabled(bool enabled)
    {
        if (Instance == null) return;
        Instance.sfxEnabled = enabled;
        PlayerPrefs.SetInt(SfxPrefKey, enabled ? 1 : 0);
    }

    public static bool IsMusicEnabled => Instance == null || Instance.musicEnabled;
    public static bool IsSfxEnabled => Instance == null || Instance.sfxEnabled;

    // Assigns (and optionally starts) the persistent background-music track from code -
    // not required if backgroundMusic is assigned in the Inspector (Awake already starts
    // it), but available for scenes/scripts that want to switch tracks intentionally.
    public static void SetMusicClip(AudioClip clip, bool playImmediately = true)
    {
        if (Instance == null || Instance.musicSource == null) return;
        if (Instance.musicSource.clip == clip && Instance.musicSource.isPlaying) return;
        Instance.musicSource.clip = clip;
        if (playImmediately && clip != null) Instance.musicSource.Play();
    }

    // Routes one-shot SFX (button clicks, correct/wrong/level-complete sounds) through
    // here so they all respect the Sound Effects toggle in real time instead of each
    // playing unconditionally. No-ops safely if clip is null (unassigned) or SFX is off.
    // The position parameter is kept for call-site compatibility but no longer used for
    // 3D placement - playback goes through the persistent sfxSource (see Awake) instead
    // of AudioSource.PlayClipAtPoint so the sound survives an immediate scene change.
    public static void PlaySfx(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip == null) return;
        if (Instance == null || !Instance.sfxEnabled) return;
        Instance.sfxSource.PlayOneShot(clip, volume);
    }

    // Shared button-click sound, called from GameUIHelper.CreateWoodButton/CreateFlatButton
    // so every button in the game gets one automatically once buttonClickSound is assigned.
    public static void PlayButtonClick()
    {
        if (Instance == null) return;
        PlaySfx(Instance.buttonClickSound, Vector3.zero);
    }
}
