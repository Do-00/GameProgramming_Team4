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
            DontDestroyOnLoad(gameObject); // 씬이 바뀌어도 음악은 안 꺼짐
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // ? 처음부터 다시 재생하는 함수 추가
    public void PlayBGM()
    {
        if (bgmSource != null)
        {
            bgmSource.time = 0f; // 재생 위치를 맨 처음(0초)으로 되돌림
            bgmSource.Play();    // 재생 시작
        }
    }

    public void StopBGM()
    {
        if (bgmSource != null)
        {
            bgmSource.Stop();
        }
    }

    // 나중에 서서히 작아지는 페이드 아웃 기능이 필요하면 아래 함수를 쓰세요
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