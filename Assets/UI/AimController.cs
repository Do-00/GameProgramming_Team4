using UnityEngine;
using UnityEngine.UI;

public class AimController : MonoBehaviour
{
    [Header("조준선 이미지")]
    [SerializeField] private Image defaultAim;
    [SerializeField] private Image interactAim;
    [SerializeField] private Image foodAim;

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
        if (playerMovement == null || !playerMovement.IsOwner || playerCamera == null) return;
        CheckAim();
    }

    private void CheckAim()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            if (hit.collider.CompareTag("Food")) { ShowAim(foodAim); return; }
            if (hit.collider.CompareTag("StartButton")) { ShowAim(interactAim); return; }
        }

        ShowAim(defaultAim);
    }

    private void ShowAim(Image aimToShow)
    {
        defaultAim?.gameObject.SetActive(defaultAim == aimToShow);
        interactAim?.gameObject.SetActive(interactAim == aimToShow);
        foodAim?.gameObject.SetActive(foodAim == aimToShow);
    }
}
