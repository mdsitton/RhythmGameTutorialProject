using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class TimeManager : MonoBehaviour
{

    private double startingGameTime = 0;
    private double startingSystemTime = 0;
    private double firstDspTime = 0;

    private double audioStartTime;
    private double currentTime;

    AudioSource audioSource;

    [SerializeField]
    private AudioSource musicSource;

    double systemUnityTimeOffset;
    
    public void CalcTimeOffset()
    {
        var systemTime = GetTime();
        var unityTime = Time.realtimeSinceStartupAsDouble;
        systemUnityTimeOffset = systemTime - unityTime;
        currentTime = unityTime;
    }

    // Using this because it's threadsafe and unity's Time api is not
    // This is being translated into the same starting position as Time.realtimeSinceStartupAsDouble
    // And the offset is measured at the start of each frame to compensate for any drift
    private double GetTime()
    {
        return (System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency) - systemUnityTimeOffset;
    }

    public void Start()
    {
        sourceStartTime = 0;
        CalcTimeOffset();
        startingSystemTime = GetTime();
        startingGameTime = Time.realtimeSinceStartupAsDouble;
        Debug.Log(startingSystemTime - startingGameTime);
        audioSource = GetComponent<AudioSource>();
        firstDspTime = lastDspTime = AudioSettings.dspTime;
    }

    private double lastDspTime;
    private double dspUpdatePeriod;
    private double lastDspUpdatePeriod;

    // This is used to get the exact time that the next audio buffer is switched to
    void OnAudioFilterRead(float[] data, int channels)
    {
        double currentTime = GetTime();

        lastDspUpdatePeriod = dspUpdatePeriod;
        dspUpdatePeriod = (AudioSettings.dspTime - lastDspTime);

        // DSP time isn't updated until after OnAudioFilterRead runs from what i can tell.
        // This typically gives an exact estimation of the next dspTime
        double nextDspTime = AudioSettings.dspTime + dspUpdatePeriod;

        if (audioDspScheduledTime > 0.0 && audioDspScheduledTime == nextDspTime)
        {
            audioStartTime = GetTime();
        }
        lastDspTime = AudioSettings.dspTime;
    }

    public double GetCurrentAudioTime()
    {
        return (currentTime - audioStartTime) + sourceStartTime;
    }

    private double audioGametimeOffset = 0.0f;

    bool audioHasBeenScheduled = false;

    double audioDspScheduledTime;

    bool isPlaying = false;

    public void Play()
    {
        isPlaying = true;
    }

    private double sourceStartTime;

    public void Pause()
    {
        isPlaying = false;
        sourceStartTime = musicSource.time;
        audioHasBeenScheduled = false;
        audioDspScheduledTime = 0.0;
        musicSource.Stop();
    }

    void Update()
    {
        CalcTimeOffset();
        // Make sure that the dspUpdatePeriod caculation has been found before scheduling playback
        if (isPlaying && dspUpdatePeriod != 0 && lastDspUpdatePeriod == dspUpdatePeriod && !audioHasBeenScheduled)
        {
            // Play 2 update periods in the future
            double playTime = AudioSettings.dspTime+(dspUpdatePeriod*2);
            audioDspScheduledTime = playTime;
            musicSource.PlayScheduled(playTime); 
            audioHasBeenScheduled = true;
        }
        Debug.Log($"{musicSource.time} {musicSource.isPlaying} {GetCurrentAudioTime()}");
    }
}