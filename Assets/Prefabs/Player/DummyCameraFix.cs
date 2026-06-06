using System.Collections;
using UnityEngine;

public class DummyCameraFix : MonoBehaviour
{
    private void Start()
    {
        // 게임이 시작되면 타이밍을 재정렬하는 코루틴을 가동합니다.
        StartCoroutine(RefreshCameraRoutine());
    }

    private IEnumerator RefreshCameraRoutine()
    {
        // 1. 첫 프레임에 카메라 오브젝트 자체를 비활성화 (인스펙터 맨 위 체크박스 해제)
        gameObject.SetActive(false);

        // 2. 유니티가 UI와 렌더 텍스처 초기화를 끝마칠 때까지 딱 한 프레임 쉽니다.
        yield return null;

        // 3. 다시 오브젝트를 활성화 (체크박스 켜기)
        gameObject.SetActive(true);

        Debug.Log("[시스템] 에디터 버그 방지를 위해 메인 카메라를 강제 재부팅했습니다.");
    }
}