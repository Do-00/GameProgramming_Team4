using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class AimController : MonoBehaviour
{
    [Header("에임 이미지")]
    [SerializeField] private Image defaultAim;      // 기본 에임
    [SerializeField] private Image interactAim;     // StartButton 등 상호작용 에임
    [SerializeField] private Image foodAim;         // 음식 에임 (섭취)

    [Header("설정")]
    [SerializeField] private float interactDistance = 4f;

    private PlayerMovement playerMovement;
    private Camera playerCamera;

    private void Start()
    {
        playerMovement = GetComponentInParent<PlayerMovement>();
        playerCamera = playerMovement.GetComponentInChildren<Camera>();

        ShowAim(defaultAim);
    }

    private void Update()
    {
        if (playerMovement == null || !playerMovement.IsOwner) return;
        if (playerCamera == null) return;

        CheckAim();
    }

    private void CheckAim()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            // Food 최우선
            if (hit.collider.CompareTag("Food"))
            {
                ShowAim(foodAim);
                return;
            }

            // StartButton 등 기타 상호작용
            if (hit.collider.CompareTag("StartButton"))
            {
                ShowAim(interactAim);
                return;
            }
        }

        ShowAim(defaultAim);
    }

    private void ShowAim(Image aimToShow)
    {
        if (defaultAim != null) defaultAim.gameObject.SetActive(defaultAim == aimToShow);
        if (interactAim != null) interactAim.gameObject.SetActive(interactAim == aimToShow);
        if (foodAim != null) foodAim.gameObject.SetActive(foodAim == aimToShow);
    }
}