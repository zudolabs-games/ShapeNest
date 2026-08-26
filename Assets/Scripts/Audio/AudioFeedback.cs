using UnityEngine;

/// <summary>
/// Scene-scoped one-shot gameplay audio. Fire-and-forget; never waits or drives gameplay.
/// </summary>
[DisallowMultipleComponent]
public class AudioFeedback : MonoBehaviour
{
    [Header("Clips")]
    [SerializeField]
    [Tooltip("Played once when a drag successfully begins.")]
    private AudioClip dragStartClip;

    [SerializeField]
    [Tooltip("Played once per actual cell hop.")]
    private AudioClip hopClip;

    [SerializeField]
    [Tooltip("Played when matching nest-entry animation begins.")]
    private AudioClip nestEntryClip;

    [SerializeField]
    [Tooltip("Played when the match/merge effect begins.")]
    private AudioClip matchClip;

    [SerializeField]
    [Tooltip("Played once when the level is completed.")]
    private AudioClip levelCompleteClip;

    [SerializeField]
    [Tooltip("Played once when the session timer reaches 0:00.")]
    private AudioClip timeUpClip;

    [SerializeField]
    [Tooltip("Played once for a rejected swipe or blocked hop. Optional.")]
    private AudioClip blockedClip;

    [SerializeField]
    [Tooltip("Played once for a partial chain-cell consume. Falls back to matchClip.")]
    private AudioClip chainMatchClip;

    [SerializeField]
    [Tooltip("Played once for a nested inner-layer consume. Falls back to matchClip.")]
    private AudioClip nestedMatchClip;

    [SerializeField]
    [Tooltip("Played once when a piece is fully consumed. Falls back to matchClip.")]
    private AudioClip fullConsumeClip;

    [Header("Volumes")]
    [SerializeField]
    [Range(0f, 1f)]
    private float masterVolume = 1f;

    [SerializeField]
    [Range(0f, 1f)]
    private float dragStartVolume = 0.35f;

    [SerializeField]
    [Range(0f, 1f)]
    private float hopVolume = 0.25f;

    [SerializeField]
    [Range(0f, 1f)]
    private float nestEntryVolume = 0.45f;

    [SerializeField]
    [Range(0f, 1f)]
    private float matchVolume = 0.6f;

    [SerializeField]
    [Range(0f, 1f)]
    private float levelCompleteVolume = 0.7f;

    [SerializeField]
    [Range(0f, 1f)]
    private float timeUpVolume = 0.7f;

    [SerializeField]
    [Range(0f, 1f)]
    private float blockedVolume = 0.22f;

    [SerializeField]
    [Range(0f, 1f)]
    private float chainMatchVolume = 0.55f;

    [SerializeField]
    [Range(0f, 1f)]
    private float nestedMatchVolume = 0.42f;

    [SerializeField]
    [Range(0f, 1f)]
    private float fullConsumeVolume = 0.72f;

    [Header("Hop Pitch")]
    [SerializeField]
    [Tooltip("Minimum pitch for hop one-shots.")]
    private float hopPitchMin = 0.96f;

    [SerializeField]
    [Tooltip("Maximum pitch for hop one-shots.")]
    private float hopPitchMax = 1.04f;

    private AudioSource audioSource;
    private bool soundEnabled = true;
    private int chainMatchStreak;
    private int lastBlockedFrame = -1;
    private string cueTrace = string.Empty;

    public bool SoundEnabled
    {
        get => soundEnabled;
        set => soundEnabled = value;
    }

    public string LastCue { get; private set; }

    /// <summary>Logical cue name of the last recorded event (independent of clip asset names).</summary>
    public string LastLogicalCue { get; private set; }

    public int LogicalCueCount { get; private set; }

    /// <summary>Pipe-separated logical cue names for this session. Cleared on level load.</summary>
    public string CueTrace => cueTrace;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.dopplerLevel = 0f;
    }

    public void PlayDragStart()
    {
        RecordLogical("drag");
        PlayOneShot(dragStartClip, dragStartVolume, 1f);
    }

    public void PlayHop()
    {
        RecordLogical("hop");
        float pitch = Random.Range(hopPitchMin, hopPitchMax);
        PlayOneShot(hopClip, hopVolume, pitch);
    }

    public void PlayNestEntry()
    {
        RecordLogical("nestEntry");
        PlayOneShot(nestEntryClip, nestEntryVolume, 1f);
    }

    public void PlayMatch()
    {
        RecordLogical("match");
        PlayOneShot(matchClip, matchVolume, 1f);
    }

    public void PlayLevelComplete()
    {
        RecordLogical("complete");
        PlayOneShot(levelCompleteClip, levelCompleteVolume, 1f);
    }

    public void PlayTimeUp()
    {
        RecordLogical("failure");
        PlayOneShot(timeUpClip, timeUpVolume, 1f);
    }

    public void PlayFailure()
    {
        PlayTimeUp();
    }

    public void PlayBlocked()
    {
        if (Time.frameCount == lastBlockedFrame)
        {
            return;
        }

        lastBlockedFrame = Time.frameCount;
        RecordLogical("blocked");
        AudioClip clip = blockedClip != null ? blockedClip : hopClip;
        PlayOneShot(clip, blockedVolume, 0.84f);
    }

    public void PlayChainMatch()
    {
        RecordLogical("chain");
        AudioClip clip = chainMatchClip != null ? chainMatchClip : matchClip;
        float pitch = Mathf.Min(1.2f, 1f + chainMatchStreak * 0.05f);
        chainMatchStreak++;
        PlayOneShot(clip, chainMatchVolume, pitch);
    }

    public void PlayNestedMatch()
    {
        RecordLogical("nested");
        AudioClip clip = nestedMatchClip != null ? nestedMatchClip : matchClip;
        PlayOneShot(clip, nestedMatchVolume, 1.08f);
    }

    public void PlayFullConsume()
    {
        chainMatchStreak = 0;
        RecordLogical("full");
        AudioClip clip = fullConsumeClip != null ? fullConsumeClip : matchClip;
        PlayOneShot(clip, fullConsumeVolume, 1f);
    }

    /// <summary>
    /// One logical consume cue. Inner-layer, full-piece, and chain-partial are mutually exclusive.
    /// </summary>
    public void PlayLogicalMatch(bool consumedInnerLayer, bool fullyConsumed)
    {
        if (consumedInnerLayer && !fullyConsumed)
        {
            PlayNestedMatch();
            return;
        }

        if (fullyConsumed)
        {
            PlayFullConsume();
            return;
        }

        PlayChainMatch();
    }

    public void ResetSessionCues()
    {
        chainMatchStreak = 0;
        lastBlockedFrame = -1;
        cueTrace = string.Empty;
        LastLogicalCue = null;
        LogicalCueCount = 0;
    }

    private void RecordLogical(string name)
    {
        LastLogicalCue = name;
        LogicalCueCount++;
        cueTrace = string.IsNullOrEmpty(cueTrace) ? name : cueTrace + "|" + name;
    }

    private void PlayOneShot(AudioClip clip, float volume, float pitch)
    {
        if (clip == null || audioSource == null)
        {
            return;
        }

        LastCue = clip.name;
        if (!soundEnabled)
        {
            return;
        }

        float previousPitch = audioSource.pitch;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, masterVolume * volume);
        audioSource.pitch = previousPitch;
    }
}
