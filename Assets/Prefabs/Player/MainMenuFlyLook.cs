using UnityEngine;

public class MainMenuFlyLook : MonoBehaviour
{
    [Header("머리 뼈 연결")]
    public Transform headBone;

    [Header("회전 설정")]
    public float maxYaw = 45f;
    public float maxPitch = 30f;
    public float lookSpeed = 5f;

    [Header("방향 반전")]
    public bool invertX = true;
    public bool invertY = false;

    private Quaternion initialRotation;

    // ? [추가됨] 위치 계산에 사용할 메인 카메라
    private Camera mainCam;

    void Start()
    {
        mainCam = Camera.main; // 시작할 때 메인 카메라를 찾아둡니다.

        if (headBone != null)
        {
            initialRotation = headBone.localRotation;
        }
    }

    void LateUpdate()
    {
        if (headBone == null || mainCam == null) return;

        // 1. ? [핵심 수정] 파리 머리의 3D 위치를 2D 화면 픽셀 좌표로 변환합니다!
        Vector3 headScreenPos = mainCam.WorldToScreenPoint(headBone.position);

        // 2. ? [핵심 수정] 화면 중앙이 아닌 '파리 머리 위치'를 0점으로 삼아 마우스 거리를 계산합니다.
        float mouseX = (Input.mousePosition.x - headScreenPos.x) / (Screen.width / 2f);
        float mouseY = (Input.mousePosition.y - headScreenPos.y) / (Screen.height / 2f);

        // 범위를 -1.0 ~ 1.0으로 고정
        mouseX = Mathf.Clamp(mouseX, -1f, 1f);
        mouseY = Mathf.Clamp(mouseY, -1f, 1f);

        // 3. 반전 옵션 적용
        float finalMouseX = invertX ? -mouseX : mouseX;
        float finalMouseY = invertY ? -mouseY : mouseY;

        // 4. 목표 각도 계산
        float targetYaw = finalMouseX * maxYaw;
        float targetPitch = finalMouseY * maxPitch;

        Quaternion targetRotation = initialRotation * Quaternion.Euler(targetPitch, targetYaw, 0f);

        // 5. 스무스하게 회전 적용
        headBone.localRotation = Quaternion.Slerp(headBone.localRotation, targetRotation, Time.deltaTime * lookSpeed);
    }
}