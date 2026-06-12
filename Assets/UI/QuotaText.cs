using UnityEngine;
using TMPro;

public class QuotaUI : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private TextMeshProUGUI quotaText;

    private void Update()
    {
        if (GameManager.Instance == null) return;

        int currentEggs = GameManager.Instance.eggsSubmittedThisRound.Value;
        int maxQuota = GameManager.Instance.quotaRequired.Value;
        int currentRound = GameManager.Instance.currentRound.Value;

        quotaText.text = $"ROUND {currentRound}\n할당량: ({currentEggs} / {maxQuota})";
    }
}
