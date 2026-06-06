using UnityEngine;

public class UIButtonSound : MonoBehaviour
{
    [Header("UI 소리를 낼 스피커")]
    public AudioSource uiAudioSource;

    [Header("버튼 누를 때 날 소리")]
    public AudioClip clickSound;

    // 버튼을 클릭할 때마다 실행될 함수입니다.
    // 주의: 버튼 이벤트에 연결하려면 반드시 'public'을 붙여야 합니다!
    public void PlayClickSound()
    {
        if (clickSound != null && uiAudioSource != null)
        {
            // PlayOneShot을 써야 버튼을 연타해도 소리가 씹히지 않고 잘 납니다.
            uiAudioSource.PlayOneShot(clickSound);
        }
    }
}