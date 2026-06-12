using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [SerializeField] private AudioSource bgmSource;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>BGM을 처음부터 재생</summary>
    public void PlayBGM()
    {
        if (bgmSource == null) return;
        bgmSource.time = 0f;
        bgmSource.Play();
    }

    /// <summary>BGM을 즉시 정지</summary>
    public void StopBGM()
    {
        bgmSource?.Stop();
    }

    /// <summary>BGM을 지정 시간 동안 페이드아웃 후 정지</summary>
    public void FadeOutBGM(float duration)
    {
        StartCoroutine(FadeOutRoutine(duration));
    }

    private System.Collections.IEnumerator FadeOutRoutine(float duration)
    {
        float startVolume = bgmSource.volume;

        while (bgmSource.volume > 0)
        {
            bgmSource.volume -= startVolume * Time.deltaTime / duration;
            yield return null;
        }

        bgmSource.Stop();
        bgmSource.volume = startVolume;
    }
}
