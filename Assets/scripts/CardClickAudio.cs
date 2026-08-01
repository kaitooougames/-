using UnityEngine;

public static class CardClickAudio
{
    private const string ClipPath = "Audio/CardFlipClick";
    private static AudioClip clip;
    private static AudioSource source;

    public static void Play()
    {
        if (clip == null) clip = Resources.Load<AudioClip>(ClipPath);
        if (clip == null) return;
        if (source == null)
        {
            GameObject audioObject = new GameObject("Card Click Audio");
            Object.DontDestroyOnLoad(audioObject);
            source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.72f;
        }
        source.PlayOneShot(clip);
    }
}
