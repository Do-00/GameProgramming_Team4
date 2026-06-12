using Unity.Netcode;
using UnityEngine;

public class FlyKingTrigger : MonoBehaviour
{
    [Header("사운드")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip swallowSound;

    private void OnTriggerEnter(Collider other)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (!other.CompareTag("Egg")) return;

        NetworkObject eggNetObj = other.GetComponent<NetworkObject>();
        if (eggNetObj == null || !eggNetObj.IsSpawned) return;

        // 플레이어가 운반 중인 알(isKinematic)은 제출되지 않도록 무시
        if (other.TryGetComponent(out Rigidbody rb) && rb.isKinematic) return;

        GameManager.Instance.SubmitEggServerRpc();
        audioSource?.PlayOneShot(swallowSound);
        eggNetObj.Despawn();
    }
}
