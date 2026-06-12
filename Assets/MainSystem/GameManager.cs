using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;

    // 맵 스폰 후 물리 연산이 정착될 때까지 기다리는 시간
    private const float MapSpawnDelay = 0.15f;
    // 게임오버 UI 이미지 슬라이드쇼가 끝난 뒤 로비로 돌아가기까지 대기 시간
    private const float GameOverReturnDelay = 10f;

    [Header("UI 연결")]
    [SerializeField] private GameOverManager gameOverManager;

    [Header("공유 재화")]
    public NetworkVariable<int> sharedEggCount = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("라운드 & 할당량")]
    public NetworkVariable<int> currentRound = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> quotaRequired = new NetworkVariable<int>(
        10, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> eggsSubmittedThisRound = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [HideInInspector]
    public Vector3 startRoomPosition;

    [Header("맵 스폰 설정")]
    [SerializeField] private GameObject houseMapPrefab;
    [SerializeField] private Transform lobbySpawnPoint;
    [SerializeField] private Transform playMapSpawnPoint;

    private GameObject spawnedMap;
    private NetworkVariable<bool> isGamePlaying = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // 씬에 배치된 목표 버튼(출발/귀환)과 상호작용할 때 클라이언트가 서버에 호출
    // 게임 중이 아니면 라운드를 시작하고, 게임 중이면 할당량을 검사해 귀환 처리
    [ServerRpc(RequireOwnership = false)]
    public void TargetButtonInteractedServerRpc()
    {
        if (!IsServer) return;

        if (!isGamePlaying.Value) StartGameRound();
        else EvaluateQuotaAndReturn();
    }

    private void StartGameRound()
    {
        if (!IsServer) return;

        Debug.Log($"[GameManager] 라운드 {currentRound.Value} 시작. 목표: {quotaRequired.Value}개");

        if (houseMapPrefab != null)
        {
            spawnedMap = Instantiate(houseMapPrefab, playMapSpawnPoint.position, Quaternion.identity);
            NetworkObject mapNetObj = spawnedMap.GetComponent<NetworkObject>();
            if (mapNetObj != null) mapNetObj.Spawn();

            Physics.SyncTransforms();

            // 서버와 모든 클라이언트가 동일한 시드로 맵을 생성해야 레이아웃이 일치함
            MapGenerator mapGen = FindFirstObjectByType<MapGenerator>();
            if (mapGen != null)
            {
                int masterSeed = Random.Range(1, 100000);
                mapGen.BuildNewMapFromSeed(masterSeed);
                BuildFurnitureOnClientRpc(masterSeed);
            }
        }

        StartCoroutine(TeleportDelayRoutine());
        isGamePlaying.Value = true;
    }

    private IEnumerator TeleportDelayRoutine()
    {
        // 맵 생성과 물리 처리가 완료된 후 텔레포트해야 캐릭터가 바닥에 제대로 착지함
        yield return new WaitForSeconds(MapSpawnDelay);

        Vector3 spawnPos = playMapSpawnPoint != null
            ? playMapSpawnPoint.position + Vector3.up * 1.5f
            : new Vector3(0f, 1002f, 0f);

        TeleportAllPlayers(spawnPos);
    }

    [ClientRpc]
    private void BuildFurnitureOnClientRpc(int seed)
    {
        if (IsServer) return;

        MapGenerator mapGen = FindFirstObjectByType<MapGenerator>();
        if (mapGen != null) mapGen.BuildNewMapFromSeed(seed);
    }

    // 파리킹 입상 오브젝트에 알이 닿을 때 호출되어 이번 라운드 제출 수를 1 증가
    [ServerRpc(RequireOwnership = false)]
    public void SubmitEggServerRpc()
    {
        if (!IsServer) return;

        eggsSubmittedThisRound.Value += 1;
        Debug.Log($"[GameManager] 알 제출. {eggsSubmittedThisRound.Value} / {quotaRequired.Value}");
    }

    private void EvaluateQuotaAndReturn()
    {
        if (!IsServer) return;

        if (eggsSubmittedThisRound.Value >= quotaRequired.Value)
        {
            Debug.Log($"[GameManager] 할당량 달성! {eggsSubmittedThisRound.Value}개 제출.");

            // 이번 라운드 제출량을 공유 재화에 합산한 뒤 다음 라운드를 준비
            sharedEggCount.Value += eggsSubmittedThisRound.Value;
            currentRound.Value += 1;
            quotaRequired.Value = CalculateNextQuota(currentRound.Value);
            eggsSubmittedThisRound.Value = 0;

            ReturnToLobby();
        }
        else
        {
            TriggerGameOver();
        }
    }

    // 할당량 공식: 기본값 3에서 시작해 라운드마다 선형(×5)과 비선형(×2.5) 증가를 합산
    // 예) 1라운드=3, 2라운드=10, 3라운드=19, 4라운드=30 ...
    private int CalculateNextQuota(int round)
    {
        return 3 + (round - 1) * 5 + (int)(round * 2.5f);
    }

    // 전 팀 전멸 또는 귀환 시 할당량 미달이면 게임오버 처리
    // 모든 수치를 초기화하고 게임오버 UI를 보여준 뒤 일정 시간 후 로비로 복귀
    public void TriggerGameOver()
    {
        Debug.Log($"[GameManager] 게임오버. 라운드: {currentRound.Value}");

        currentRound.Value = 1;
        quotaRequired.Value = 3;
        eggsSubmittedThisRound.Value = 0;
        sharedEggCount.Value = 0;

        if (gameOverManager != null)
        {
            gameOverManager.TriggerGameOverUI();
            StartCoroutine(DelayedReturnToLobby(GameOverReturnDelay));
        }
        else
        {
            ReturnToLobby();
        }
    }

    private IEnumerator DelayedReturnToLobby(float delay)
    {
        yield return new WaitForSeconds(delay);
        ReturnToLobby();

        if (gameOverManager != null)
            gameOverManager.HideGameOverUIClientRpc();
    }

    private void ReturnToLobby()
    {
        if (!IsServer) return;

        Vector3 lobbyPos = lobbySpawnPoint != null
            ? lobbySpawnPoint.position + Vector3.up * 1f
            : new Vector3(0f, 1.5f, 0f);

        TeleportAllPlayers(lobbyPos);
        CleanUpAllSpawnedObjects();

        if (spawnedMap != null)
        {
            NetworkObject mapNetObj = spawnedMap.GetComponent<NetworkObject>();
            if (mapNetObj != null && mapNetObj.IsSpawned) mapNetObj.Despawn();
            Destroy(spawnedMap);
        }

        isGamePlaying.Value = false;
    }

    private void CleanUpAllSpawnedObjects()
    {
        if (!IsServer) return;

        CleanUpByTag("Food");
        CleanUpByTag("Enemy");
        CleanUpByTag("Cobweb");
        CleanUpByTag("Egg");
    }

    // 해당 태그의 모든 오브젝트를 네트워크 동기화를 유지하면서 제거
    // NetworkObject가 있으면 Despawn, 없으면 일반 Destroy 사용
    private void CleanUpByTag(string tag)
    {
        foreach (GameObject obj in GameObject.FindGameObjectsWithTag(tag))
        {
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned) netObj.Despawn();
            else Destroy(obj);
        }
    }

    // 사망한 플레이어는 텔레포트 대신 부활 처리를 해야 Rigidbody와 콜라이더가 정상 복구됨
    private void TeleportAllPlayers(Vector3 targetPosition)
    {
        foreach (PlayerMovement player in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (player.isDead.Value) player.ReviveServerRpc(targetPosition);
            else player.TeleportClientRpc(targetPosition);
        }
    }
}
