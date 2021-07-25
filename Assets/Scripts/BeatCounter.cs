using System;
using UnityEngine;

public class BeatCounter : MonoBehaviour
{
    [SerializeField] private TimeManager manager;
    [SerializeField] private float bpm;
    [SerializeField] private float beatTolerance = 0.05f;

    private double beatInterval;

    public Action OnBeatEvent;

    private void Start()
    {
        beatInterval = 60 / bpm;
    }

    private void Update()
    {
        if (!manager.IsPlaying()) return;

        if (Math.Abs(manager.GetCurrentAudioTime() % beatInterval) < beatTolerance)
        {
            Beat();
        }
    }

    private void Beat()
    {
        OnBeatEvent?.Invoke();
    }

    private void OnGUI()
    {
        if (manager.IsPlaying()) return;

        if (GUILayout.Button("Start", GUILayout.Height(70), GUILayout.Width(200)))
        {
            manager.Play();
        }
    }
}