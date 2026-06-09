using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TMPro;

public class ResurrectionShrine : NetworkBehaviour
{
    [Header("부활 설정")]
    [SerializeField] private int baseCost = 2; // 처음 부활 시 필요한 알 개수
    [SerializeField] private int costIncrement = 2; // 다음 부활마다 추가로 요구되는 알 개수
    [SerializeField] private Transform revivePoint; // 플레이어가 스폰될 제단 앞 위치

    [Header("UI 설정")]
    [SerializeField] private TextMeshProUGUI shrineText; // 제단 위에 띄울 텍스트
    [SerializeField] private float textVisibleDistance = 7f; // 텍스트가 보이는 거리

    // 네트워크 동기화 변수들
    private NetworkVariable<int> reviveCount = new NetworkVariable<int>(0);
    private NetworkVariable<int> currentEggsInShrine = new NetworkVariable<int>(0);

    // 제단 구역 안에 들어온 알들을 추적하는 리스트 (서버 전용)
    private List<NetworkObject> eggList = new List<NetworkObject>();

    void Update()
    {
        if (shrineText == null) return;

        // 현재 필요한 알 개수 계산
        int requiredEggs = baseCost + (reviveCount.Value * costIncrement);

        // 로컬 플레이어와의 거리를 계산하여 텍스트 표시/숨김 처리
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayer != null)
            {
                float dist = Vector3.Distance(transform.position, localPlayer.transform.position);
                if (dist <= textVisibleDistance)
                {
                    shrineText.enabled = true;
                    // 조건 달성 여부에 따라 색상을 다르게 표시할 수도 있습니다.
                    shrineText.text = $"[부활 제단]\n필요한 알: ({currentEggsInShrine.Value} / {requiredEggs})";
                }
                else
                {
                    shrineText.enabled = false;
                }
            }
        }
    }

    // 물리적으로 제단에 알이 들어왔을 때 카운트
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

    // 누군가 제단 밖으로 알을 다시 주워서 빼갔을 때 카운트 차감
    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Egg"))
        {
            NetworkObject netObj = other.GetComponent<NetworkObject>();
            if (netObj != null && eggList.Contains(netObj))
            {
                eggList.Remove(netObj);
                currentEggsInShrine.Value = eggList.Count;
            }
        }
    }

    // 플레이어가 제단의 스위치를 눌렀을 때 작동하는 서버 로직
    [ServerRpc(RequireOwnership = false)]
    public void InteractReviveButtonServerRpc()
    {
        if (!IsServer) return;

        // 파괴되거나 디스폰된 알이 리스트에 남아있을 수 있으므로 정리
        eggList.RemoveAll(egg => egg == null || !egg.IsSpawned);
        currentEggsInShrine.Value = eggList.Count;

        int requiredEggs = baseCost + (reviveCount.Value * costIncrement);

        if (currentEggsInShrine.Value >= requiredEggs)
        {
            PlayerMovement targetPlayer = GetFirstDeadPlayer();

            if (targetPlayer != null)
            {
                // 1. 필요한 개수만큼만 알을 소모(파괴)합니다.
                for (int i = 0; i < requiredEggs; i++)
                {
                    eggList[i].Despawn();
                }
                eggList.RemoveRange(0, requiredEggs);
                currentEggsInShrine.Value = eggList.Count;

                // 2. 부활 요구치 증가 및 플레이어 부활 실행
                reviveCount.Value++;
                targetPlayer.ReviveServerRpc(revivePoint.position);

                Debug.Log($"[부활 제단] 플레이어 부활 성공! 다음 요구량: {baseCost + (reviveCount.Value * costIncrement)}개");
            }
            else
            {
                Debug.Log("[부활 제단] 죽은 팀원이 없습니다!");
            }
        }
        else
        {
            Debug.Log("[부활 제단] 알이 부족합니다!");
        }
    }

    // 가장 먼저 죽은(timeOfDeath가 가장 작은) 플레이어를 찾는 로직
    private PlayerMovement GetFirstDeadPlayer()
    {
        PlayerMovement[] allPlayers = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        PlayerMovement firstDead = null;
        float oldestTime = float.MaxValue;

        foreach (PlayerMovement p in allPlayers)
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