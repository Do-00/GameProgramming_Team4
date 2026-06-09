using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using System.Collections;

public class GameOverManager : NetworkBehaviour
{
    [Header("UI 구성 요소")]
    [SerializeField] private GameObject gameOverCanvas;
    [SerializeField] private Image targetImage;

    [Header("시네마틱 설정")]
    [Tooltip("3초마다 바뀔 이미지들을 여기에 순서대로 넣으세요.")]
    [SerializeField] private Sprite[] sequenceSprites;
    [SerializeField] private float timePerImage = 3.0f; // 3초 간격

    // GameManager가 호출하는 함수
    public void TriggerGameOverUI()
    {
        if (!IsServer) return;

        // 모든 클라이언트의 화면을 띄웁니다.
        ShowGameOverUIClientRpc();
    }

    [ClientRpc]
    private void ShowGameOverUIClientRpc()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.StopBGM();
        }

        if (gameOverCanvas != null) gameOverCanvas.SetActive(true);

        // 코루틴으로 이미지를 순차적으로 바꿉니다.
        if (sequenceSprites.Length > 0 && targetImage != null)
        {
            StartCoroutine(ClientImageSequenceRoutine());
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private IEnumerator ClientImageSequenceRoutine()
    {
        for (int i = 0; i < sequenceSprites.Length; i++)
        {
            targetImage.sprite = sequenceSprites[i];
            yield return new WaitForSeconds(timePerImage);
        }
    }

    // 로비로 돌아온 직후 캔버스를 꺼주기 위한 함수
    [ClientRpc]
    public void HideGameOverUIClientRpc()
    {
        if (gameOverCanvas != null)
        {
            gameOverCanvas.SetActive(false);
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBGM();
        }
    }
}