using UnityEngine;

public class UIButtonSound : MonoBehaviour
{
    [Header("UI 사운드")]
    public AudioSource uiAudioSource;
    public AudioClip clickSound;

    // 버튼 이벤트에서 직접 호출할 수 있도록 public
    public void PlayClickSound()
    {
        if (clickSound != null && uiAudioSource != null)
            uiAudioSource.PlayOneShot(clickSound);
    }
}
