
using UnityEngine;

public class BeatTest : MonoBehaviour
{
    
    [SerializeField] private TimeManager manager;
    [SerializeField] private BeatCounter beat;
    [SerializeField] private SpriteRenderer img;

    Vector2 startingSize;

    private void Start()
    {
        beat.OnBeatEvent += OnBeatShow;
        startingSize = img.transform.localScale;
    }

    private void OnBeatShow()
    {
        img.transform.localScale = startingSize;
    }

    private void Update()
    {
        var xSize = img.transform.localScale.x - Time.deltaTime;
        var ySize = img.transform.localScale.y - Time.deltaTime;
        img.transform.localScale = new Vector2(xSize, ySize);
    }
        
}