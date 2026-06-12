using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TMPro;

public class ResurrectionShrine : NetworkBehaviour
{
    [Header("부활 비용")]
    [SerializeField] private int baseCost = 2;       // 첫 부활 비용
    [SerializeField] private int costIncrement = 2;  // 부활할 때마다 추가되는 비용
    [SerializeField] private Transform revivePoint;

    [Header("UI 연결")]
    [SerializeField] private TextMeshProUGUI shrineText;
    [SerializeField] private float textVisibleDistance = 7f;

    private NetworkVariable<int> reviveCount = new NetworkVariable<int>(0);
    private NetworkVariable<int> currentEggsInShrine = new NetworkVariable<int>(0);

    // 트리거 영역 안에 들어와 있는 알 목록 (서버 전용)
    private List<NetworkObject> eggList = new List<NetworkObject>();

    private void Update()
    {
        if (shrineText == null) return;

        int requiredEggs = GetRequiredEggs();

        var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (localPlayer != null)
        {
            float dist = Vector3.Distance(transform.position, localPlayer.transform.position);
            shrineText.enabled = dist <= textVisibleDistance;

            if (shrineText.enabled)
                shrineText.text = $"[부활 제단]\n필요한 알: ({currentEggsInShrine.Value} / {requiredEggs})";
        }
    }

    // 알이 트리거에 들어오면 eggList에 추가하고 NetworkVariable로 동기화
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Egg"))
        {
            NetworkObject netObj = other.GetComponent<NetworkObject>();
            if (netObj != null && !eggList.Contains(netObj))
            {
                eggList.Add(netObj);
                currentEggsInShrine.Value = eggList.Count;
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Egg"))
        {
            NetworkObject netObj = other.GetComponent<NetworkObject>();
            if (netObj != null && eggList.Remove(netObj))
                currentEggsInShrine.Value = eggList.Count;
        }
    }

    // 부활 버튼을 플레이어가 E키로 눌렀을 때 서버에서 처리
    // 처리 순서: 디스폰된 알 정리 → 비용 검사 → 대상 선정 → 알 소모 → 부활
    [ServerRpc(RequireOwnership = false)]
    public void InteractReviveButtonServerRpc()
    {
        if (!IsServer) return;

        // 누군가 알을 집어가거나 이미 Despawn된 알이 목록에 남아있을 수 있으므로 먼저 정리
        eggList.RemoveAll(egg => egg == null || !egg.IsSpawned);
        currentEggsInShrine.Value = eggList.Count;

        int requiredEggs = GetRequiredEggs();

        if (currentEggsInShrine.Value < requiredEggs)
        {
            Debug.Log("[부활 제단] 알이 부족합니다.");
            return;
        }

        PlayerMovement targetPlayer = GetFirstDeadPlayer();
        if (targetPlayer == null)
        {
            Debug.Log("[부활 제단] 사망한 플레이어가 없습니다.");
            return;
        }

        // 목록 앞쪽부터 requiredEggs개를 소모
        for (int i = 0; i < requiredEggs; i++)
            eggList[i].Despawn();

        eggList.RemoveRange(0, requiredEggs);
        currentEggsInShrine.Value = eggList.Count;

        reviveCount.Value++;
        targetPlayer.ReviveServerRpc(revivePoint.position);

        Debug.Log($"[부활 제단] 부활 완료. 다음 부활 비용: {GetRequiredEggs()}개");
    }

    // 부활 비용 = 기본값 + (지금까지 부활 횟수 × 증가량)
    // reviveCount는 부활 성공 후 증가하므로 다음 비용은 항상 이번 비용보다 높음
    private int GetRequiredEggs() => baseCost + (reviveCount.Value * costIncrement);

    // 사망한 플레이어 중 가장 먼저 죽은 사람을 우선 부활
    // timeOfDeath(Time.time 기준)가 작을수록 먼저 죽은 것
    private PlayerMovement GetFirstDeadPlayer()
    {
        PlayerMovement firstDead = null;
        float oldestTime = float.MaxValue;

        foreach (PlayerMovement p in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (p.isDead.Value && p.timeOfDeath.Value < oldestTime)
            {
                oldestTime = p.timeOfDeath.Value;
                firstDead = p;
            }
        }

        return firstDead;
    }
}
