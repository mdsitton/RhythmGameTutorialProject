// Copyright 2021 Matthew Sitton

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// This license just applies to this file, this is seperate from the original codebase licsense
using UnityEngine;

// The AudioSource this is requiring isn't the music source
// this is setup to start on awake, and loop so we always get OnAudioFilterRead called on the audio thread
[RequireComponent(typeof(AudioSource))]
public class TimeManager : MonoBehaviour
{
    private double firstDspTime = 0;

    private double audioStartTime;
    private double currentTime;
    private double lastTime;

    [SerializeField]
    private double preStartTime = 1;

    [SerializeField]
    private AudioSource musicSource;

    double systemUnityTimeOffset;

    double lastFrameTime;
    double currentFrameTime;

    bool gameThreadStall;
    double syncDelta;

    double syncSpeedupRate;

    double currentSystemTime;
    double currentUnityTime;

    public double UnityRealtimeToSongTime(double time)
    {
        // calculate time offset relative to current frame's realtimeSinceStartup
        // then offset the current audio time to get the correct song time
        return GetCurrentAudioTime() + (currentUnityTime - time);
    }
    private bool initialTimeSet = false;
    public void ProcessAudioTime()
    {
        // Measure the time offset between unity realtimeSinceStartup and System.Diags stopwatch time
        // This is used for offsetting when checking time from the audio thread to match unity time
        currentSystemTime = GetTimeImpl();
        currentUnityTime = Time.realtimeSinceStartupAsDouble;
        systemUnityTimeOffset = currentSystemTime - currentUnityTime;

#if !UNITY_WEBGL

        // Using Time.timeAsDouble to calculate Time.deltaTime as a double since unity doesn't have an api for this
        lastFrameTime = currentFrameTime;
        currentFrameTime = Time.timeAsDouble;
        var doubleDelta = currentFrameTime - lastFrameTime;
        // You could consider this the audio thread "pinging" the game thread.
        // This calculates the latency between when the audio thread was ran and when the game thread runs
        // and if the game thread is greater than dspUpdatePeriod (the update period between calls of the audio thread)
        // then it will consider this a game thread stall and activate the re-syncronization code
        if (!gameThreadStall && gameThreadLatencyAck)
        {
            syncDelta = Time.realtimeSinceStartupAsDouble - audioThreadTimeLatencyAck;
            gameThreadLatencyAck = false;

            if (syncDelta > dspUpdatePeriod)
            {
                // Calculate a more accurate sync delta from the realtime value
                var syncDeltaAccurate = ((currentUnityTime - audioStartTime) + sourceStartTime) - currentTime;

                // If syncDeltaAccurate is more than 100ms off use the original value.
                // This likely means the editor was paused and resumed, in this case check time source and sync to that
                if ((syncDeltaAccurate - syncDelta) < 0.1)
                {
                    var sourceDelta = musicSource.time - currentTime;
                    syncDelta = sourceDelta;
                }

                gameThreadStall = true;
            }
        }

        if (gameThreadStall)
        {
            // Doubles the speed of time until we catch up
            if (syncSpeedupRate == 0.0)
                syncSpeedupRate = 2.0;
            doubleDelta *= syncSpeedupRate;

            if (doubleDelta > syncDelta)
            {
                doubleDelta = syncDelta;
            }
            syncDelta -= doubleDelta;
        }

        if (syncDelta <= 0)
        {
            syncSpeedupRate = 0.0;
            gameThreadStall = false;
        }
#endif

#if UNITY_WEBGL
        currentTime = (audioDspScheduledTime == 0.0 ? -preStartTime : AudioSettings.dspTime - audioDspScheduledTime) + sourceStartTime;
#else
        if (audioStartTime > 0)
        {
            // This is for measuring the time offset after the song start has been scheduled and getting the exact latency offset since the start of audio playback
            if (currentTime >= 0 && !initialTimeSet)
            {
                initialTimeSet = true;
                currentTime = (currentUnityTime - audioStartTime) + sourceStartTime;
            }
            else
            {
                currentTime += doubleDelta;
            }
        }
        else if (currentTime < 0)
        {
            currentTime += doubleDelta;
        }
#endif
    }

    public double bufferLatency;

    public void Start()
    {
        currentTime = -preStartTime;
        sourceStartTime = 0;
        ProcessAudioTime();
        currentFrameTime = lastFrameTime = Time.timeAsDouble;
        firstDspTime = lastDspTime = AudioSettings.dspTime;
        AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
        bufferLatency = ((double)bufferLength * numBuffers) / AudioSettings.outputSampleRate;
    }

    private double lastDspTime;
    private double dspUpdatePeriod;
    private double lastDspUpdatePeriod;


    private double GetTimeImpl()
    {
        return (System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
    }

#if !UNITY_WEBGL

    private bool gameThreadLatencyAck = false;
    private double audioThreadTimeLatencyAck;

    // Using this because it's threadsafe and unity's Time api is not
    // This is being translated into the same starting position as Time.realtimeSinceStartupAsDouble
    // And the offset is measured at the start of each frame to compensate for any drift
    private double GetTime()
    {
        return GetTimeImpl() - systemUnityTimeOffset;
    }

    // This is used to schedule the audio playback and get the exact start time of audio to calculate latency, runs from the audio thread
    void OnAudioFilterRead(float[] data, int channels)
    {
        // Calculate the update period of the audio thread, basically how much time between calls
        // lastDspUpdatePeriod is used to determine if the update period is stable
        lastDspUpdatePeriod = dspUpdatePeriod;
        dspUpdatePeriod = (AudioSettings.dspTime - lastDspTime);

        // DSP time isn't updated until after OnAudioFilterRead runs from what i can tell.
        // This typically gives an exact estimation of the next dspTime
        double nextDspTime = AudioSettings.dspTime + dspUpdatePeriod;

        if (audioDspScheduledTime > 0.0 && audioDspScheduledTime <= nextDspTime && audioStartTime == 0)
        {
            audioStartTime = GetTime();
        }
        lastDspTime = AudioSettings.dspTime;

        // Trigger audio -> game thread latency check, if the game thread detects a latency larger than the dspUpdatePeriod
        // Then it will trigger the audio time sync code
        if (!gameThreadLatencyAck && audioDspScheduledTime > 0.0 && audioDspScheduledTime <= nextDspTime)
        {
            gameThreadLatencyAck = true;
            audioThreadTimeLatencyAck = GetTime();
        }
    }
#endif

    public double GetCurrentAudioTime()
    {
        if (!audioHasBeenScheduled)
            return 0;
        return currentTime - bufferLatency;
    }

    private double audioGametimeOffset = 0.0f;

    bool audioHasBeenScheduled = false;

    double audioDspScheduledTime;

    bool isPlaying = false;
    bool isPaused = false;

    public bool IsPlaying()
    {
        return musicSource.isPlaying;
    }

    public bool IsPaused()
    {
        return isPaused;
    }

    public void StartTime(double time)
    {
        sourceStartTime = time;
    }

    public void Play()
    {
        isPlaying = true;
        isPaused = false;
    }

    private double sourceStartTime;

    public double GetMusicLength()
    {
        return musicSource.clip == null ? float.MaxValue : Mathf.Max(0.01f, musicSource.clip.length);
    }

    public void Pause()
    {
        sourceStartTime = musicSource.time;
        musicSource.Stop();
        isPlaying = false;
        isPaused = true;
        audioStartTime = 0;
        currentTime = 0;
        audioHasBeenScheduled = false;
        audioDspScheduledTime = 0.0;
    }

    void Update()
    {
        ProcessAudioTime();
        // The following is the playback scheduling system, this Schedules the audio to start at the begining of the next
        // Audio thread invoke time so we know exactly when the audio thread should begin playing the audio for latency calulation
        // Make sure that the dspUpdatePeriod caculation has been found before scheduling playback
        // WebGL just schedules it at the correct offset in the future based on the pre start time
#if UNITY_WEBGL
        if (isPlaying && !audioHasBeenScheduled)
        {

            // Play 2 update periods in the future
            double playOffset = preStartTime;

            currentTime = -playOffset + sourceStartTime;
            musicSource.time = (float)sourceStartTime;

            audioDspScheduledTime = AudioSettings.dspTime + playOffset;
            musicSource.PlayScheduled(audioDspScheduledTime);

            audioHasBeenScheduled = true;
        }
#else
        if (isPlaying && dspUpdatePeriod != 0 && lastDspUpdatePeriod == dspUpdatePeriod && !audioHasBeenScheduled)
        {

            // Play 2 update periods in the future
            double playOffset = ((int)(preStartTime / dspUpdatePeriod)) * dspUpdatePeriod;

            currentTime = -playOffset + sourceStartTime;
            musicSource.time = (float)sourceStartTime;

            audioDspScheduledTime = AudioSettings.dspTime + playOffset;
            musicSource.PlayScheduled(audioDspScheduledTime);

            audioHasBeenScheduled = true;
        }
#endif
    }
}