using UnityEngine;

public class HeartIconPulse : MonoBehaviour
{
    [Header("크기 설정")]
    [SerializeField] private float minScale = 0.9f;
    [SerializeField] private float maxScale = 1.1f;

    [Header("속도 설정")]
    [SerializeField] private float normalSpeed = 1f;
    [SerializeField] private float dangerSpeed = 3f;
    [SerializeField] private float dangerThreshold = 0.33f;

    private Vector3 originalScale;
    private float timer = 0f;
    private PlayerMovement playerMovement;

    private void Start()
    {
        // Inspector 연결 대신 부모에서 자동으로 찾기
        originalScale = transform.localScale; 

        playerMovement = GetComponentInParent<PlayerMovement>();
    }

    private void Update()
    {
        // 내 플레이어가 아니면 실행 안 함
        if (playerMovement == null || !playerMovement.IsOwner) return;

        float healthRatio = playerMovement.currentHealth.Value / 100f;
        float staminaRatio = playerMovement.currentStamina.Value / 20f;
        bool isDanger = healthRatio <= dangerThreshold || staminaRatio <= dangerThreshold;

        float speed = isDanger ? dangerSpeed : normalSpeed;

        timer += Time.deltaTime * speed;
        float scale = Mathf.Lerp(minScale, maxScale, (Mathf.Sin(timer * Mathf.PI) + 1f) / 2f);
        transform.localScale = originalScale * scale;
    }
}