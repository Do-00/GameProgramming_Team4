using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 쉘터 트리거 영역
/// - 플레이어가 쉘터 안에 들어오면 inShelter = true
/// - 나가면 inShelter = false
/// - SkillShop이 이걸 읽어서 탭 키 입력 허용 여부 결정
/// </summary>
public class ShelterZone : MonoBehaviour
{
    // 현재 쉘터 안에 있는 내 플레이어인지 여부
    // PlayerSkill(SkillShop)에서 이걸 읽음
    public static bool IsLocalPlayerInShelter { get; private set; } = false;

    private void OnTriggerEnter(Collider other)
    {
        // 들어온 오브젝트가 내가 조종하는 플레이어인지 확인
        NetworkObject netObj = other.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsOwner)
        {
            IsLocalPlayerInShelter = true;
            Debug.Log("[쉘터] 입장");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        NetworkObject netObj = other.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsOwner)
        {
            IsLocalPlayerInShelter = false;
            Debug.Log("[쉘터] 퇴장");
        }
    }
}
