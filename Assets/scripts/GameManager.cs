using UnityEngine;

public class GameManager : MonoBehaviour
{
    private static GameManager instance;
    private AudioSource audioSource;

    [SerializeField] private AudioClip bgmClip; // 🎵 美術館2.m4a をセット

    public static GameManager Instance => instance;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        audioSource.loop = true;
        audioSource.volume = 0.5f; // 音量調整

        if (bgmClip != null)
        {
            PlayBGM(bgmClip);
        }
    }

    // 🎶 BGMを再生する
    public void PlayBGM(AudioClip clip)
    {
        if (clip != null)
        {
            audioSource.clip = clip;
            audioSource.Play();
        }
    }
}
