using Unity.Netcode;
using UnityEngine;

public class FlyKingTrigger : MonoBehaviour
{
    [Header("효과음")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip swallowSound;

    private void OnTriggerEnter(Collider other)
    {
        // 서버에서만 물리 충돌 및 점수 계산 처리를 주도합니다.
        if (!NetworkManager.Singleton.IsServer) return;

        // 들어온 물체의 태그가 "Egg" 인지 확인
        if (other.CompareTag("Egg"))
        {
            NetworkObject eggNetObj = other.GetComponent<NetworkObject>();

            // 들려있는 상태의 알은 강제로 뺏어가지 않도록 kinematic 체크
            if (eggNetObj != null && eggNetObj.IsSpawned)
            {
                if (other.TryGetComponent(out Rigidbody rb) && rb.isKinematic) return;

                // 1. 게임매니저에 알이 제출되었다고 통보하여 카운트 업!
                GameManager.Instance.SubmitEggServerRpc();

                // 2. 흡수 사운드 재생 (오브젝트가 사라지기 전에 사운드 플레이)
                if (audioSource != null && swallowSound != null)
                {
                    audioSource.PlayOneShot(swallowSound);
                }

                // 3. 서버에서 월드의 알을 완전히 삭제(Despawn) 처리
                eggNetObj.Despawn();
            }
        }
    }
}