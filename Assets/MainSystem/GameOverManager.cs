using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using System.Collections;

public class GameOverManager : NetworkBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private GameObject gameOverCanvas;
    [SerializeField] private Image targetImage;

    [Header("이미지 슬라이드쇼")]
    [Tooltip("순서대로 표시할 스프라이트 배열")]
    [SerializeField] private Sprite[] sequenceSprites;
    [SerializeField] private float timePerImage = 3.0f;

    // 서버에서 게임오버가 확정되면 이 함수로 모든 클라이언트에 UI 표시를 요청
    public void TriggerGameOverUI()
    {
        if (!IsServer) return;

        ShowGameOverUIClientRpc();
    }

    [ClientRpc]
    private void ShowGameOverUIClientRpc()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.StopBGM();

        if (gameOverCanvas != null)
            gameOverCanvas.SetActive(true);

        if (sequenceSprites.Length > 0 && targetImage != null)
            StartCoroutine(ImageSequenceRoutine());

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // 배열에 담긴 스프라이트를 순서대로 교체하며 슬라이드쇼 재생
    private IEnumerator ImageSequenceRoutine()
    {
        foreach (Sprite sprite in sequenceSprites)
        {
            targetImage.sprite = sprite;
            yield return new WaitForSeconds(timePerImage);
        }
    }

    // 로비 복귀 직전에 서버가 호출하여 게임오버 UI를 숨기고 BGM을 재개
    [ClientRpc]
    public void HideGameOverUIClientRpc()
    {
        if (gameOverCanvas != null)
            gameOverCanvas.SetActive(false);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayBGM();
    }
}
