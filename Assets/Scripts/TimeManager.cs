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
    private AudioSource musicSource;

    double systemUnityTimeOffset;

    double lastFrameTime;
    double currentFrameTime;

    bool gameThreadStall;
    double syncDelta;

    double syncSpeedupRate;
    
    public void ProcessAudioTime()
    {
        // Measure the time offset between unity realtimeSinceStartup and System.Diags stopwatch time
        // This is used for offsetting when checking time from the audio thread to match unity time
        var systemTime = GetTimeImpl();
        var unityTime = Time.realtimeSinceStartupAsDouble;
        systemUnityTimeOffset = systemTime - unityTime;

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
                var syncDeltaAccurate = ((unityTime - audioStartTime)+sourceStartTime) - currentTime;

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

        if (audioStartTime > 0)
        {
            // This is for measuring the time offset after the song start has been scheduled and getting the exact latency offset since the start of audio playback
            if (currentTime == 0)
            {
                currentTime = (unityTime - audioStartTime)+sourceStartTime;
            }
            else
            {
                currentTime += doubleDelta;
            }
        }

    }

    private double GetTimeImpl()
    {
        return (System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
    }

    // Using this because it's threadsafe and unity's Time api is not
    // This is being translated into the same starting position as Time.realtimeSinceStartupAsDouble
    // And the offset is measured at the start of each frame to compensate for any drift
    private double GetTime()
    {
        return GetTimeImpl() - systemUnityTimeOffset;
    }

    public void Start()
    {
        sourceStartTime = 0;
        ProcessAudioTime();
        currentFrameTime = lastFrameTime = Time.timeAsDouble;
        firstDspTime = lastDspTime = AudioSettings.dspTime;
    }

    private double lastDspTime;
    private double dspUpdatePeriod;
    private double lastDspUpdatePeriod;

    private bool gameThreadLatencyAck = false;
    private double audioThreadTimeLatencyAck;

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

    public double GetCurrentAudioTime()
    {
        return currentTime;
    }

    private double audioGametimeOffset = 0.0f;

    bool audioHasBeenScheduled = false;

    double audioDspScheduledTime;

    bool isPlaying = false;

    public bool IsPlaying()
    {
        return musicSource.isPlaying;
    }

    public void Play()
    {
        isPlaying = true;
    }

    private double sourceStartTime;

    public void Pause()
    {
        sourceStartTime = GetCurrentAudioTime();
        musicSource.Stop();
        isPlaying = false;
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
        if (isPlaying && dspUpdatePeriod != 0 && lastDspUpdatePeriod == dspUpdatePeriod && !audioHasBeenScheduled)
        {
            // Play 2 update periods in the future
            double playTime = AudioSettings.dspTime+(dspUpdatePeriod*2);
            audioDspScheduledTime = playTime;
            musicSource.time = (float)sourceStartTime;
            musicSource.PlayScheduled(playTime); 
            audioHasBeenScheduled = true;
        }
    }
}