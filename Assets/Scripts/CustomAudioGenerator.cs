using System;
using System.Collections.Generic;
using UnityEngine;

public class CustomAudioGenerator
{
    private static double calTempo = 120;

    private static double sustainTime = 0.1;
    private static int frequency;

    public static int sampleRate = 44100;
    public static int channels = 2;

    private static int stream = 0;

    private static float[] audio;

    private static int playbackSamplePos = 0;

    public static void SetupAudio(List<double> notes)
    {
        audio = GenerateAudio(notes);
    }

    public static List<double> GenerateNoteTimes()
    {
        var notes = new List<double>();

        double timePerBeat = 1.0 / (calTempo / 60.0);

        double measureLength = timePerBeat * 4;

        double time = measureLength;

        for (int i = 0; i < 16; ++i)
        {
            var notesPerMeasure = 4;

            var timePerNote = measureLength / notesPerMeasure;

            for (int n = 0; n < notesPerMeasure; ++n)
            {
                double note = time + (n * timePerNote);
                notes.Add(note);
            }
            time += measureLength;
        }
        return notes;
    }

    public static int GenerateSilence(float[] outputData, int sampleStart, int sampleCount)
    {
        var framePosition = 0;
        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
        {
            for (int c = 0; c < channels; ++c)
            {
                outputData[(i * channels) + c] = 0.0f;
            }
            framePosition++;
        }
        return sampleCount;
    }

    public static int GenerateTone(float[] outputData, int sampleStart, int sampleCount, float frequency)
    {
        var framePosition = 0;
        var fadeFrames = (sustainTime * 0.125) * sampleRate;
        var fadePos = sampleStart + (sampleCount - fadeFrames);
        float amplitude = 0.15f;

        var fadePerFrame = amplitude / fadeFrames;
        for (var i = sampleStart; i < sampleStart + sampleCount; ++i)
        {
            if (i >= fadePos)
            {
                amplitude -= (float)fadePerFrame;
            }

            for (int c = 0; c < channels; ++c)
            {
                outputData[(i * channels) + c] += (float)Math.Sin(2 * Math.PI * (i + framePosition) * frequency / sampleRate) * amplitude;
            }
            framePosition++;
        }

        return sampleCount;
    }

    public static float[] GenerateAudio(List<double> notes)
    {

        var audioLength = notes[notes.Count - 1] + 1;

        int audioFrameCount = Mathf.CeilToInt((float)(audioLength * sampleRate));

        float[] samples = new float[audioFrameCount * channels];

        double lastAudioTime = 0.0;
        int framePosition = 0;

        int sustainFrames = Mathf.CeilToInt((float)(sustainTime * sampleRate));

        foreach (var note in notes)
        {
            int silenceFrames = Mathf.RoundToInt((float)((note - lastAudioTime) * sampleRate));
            framePosition += GenerateSilence(samples, framePosition, silenceFrames);
            GenerateTone(samples, framePosition, sustainFrames, frequency: 110.00f);
            GenerateTone(samples, framePosition, sustainFrames, frequency: 698.46f);
            framePosition += GenerateTone(samples, framePosition, sustainFrames, frequency: 880.00f);
            lastAudioTime = note + sustainTime;
        }
        return samples;
    }

    static float[] tmpData;

}