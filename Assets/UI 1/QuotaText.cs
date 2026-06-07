using UnityEngine;
using TMPro; // TextMeshPro를 사용하기 위한 필수 선언

public class QuotaUI : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private TextMeshProUGUI quotaText;

    void Update()
    {
        // GameManager가 씬에 정상적으로 존재할 때만 작동합니다.
        if (GameManager.Instance != null)
        {
            // GameManager에서 실시간 네트워크 변수 값들을 가져옵니다.
            int currentEggs = GameManager.Instance.eggsSubmittedThisRound.Value;
            int maxQuota = GameManager.Instance.quotaRequired.Value;
            int currentRound = GameManager.Instance.currentRound.Value;

            // 화면에 보여질 텍스트 형식을 세팅합니다. 
            // \n 은 줄바꿈을 의미합니다.
            quotaText.text = $"ROUND {currentRound}\n할당량: ({currentEggs} / {maxQuota})";
        }
    }
}