using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;

    [Header("팀원 공유 데이터")]
    // 스킬 상점에서 사용하는 화폐 (할당량 성공 시 환전됨)
    public NetworkVariable<int> sharedEggCount = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("할당량(Quota) 시스템 설정")]
    // 현재 진행 중인 라운드 번호
    public NetworkVariable<int> currentRound = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 이번 라운드에 파리 대왕에게 바쳐야 하는 목표 알 개수
    public NetworkVariable<int> quotaRequired = new NetworkVariable<int>(
        10, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 이번 라운드에 현재까지 바친 알 개수
    public NetworkVariable<int> eggsSubmittedThisRound = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [HideInInspector]
    public Vector3 startRoomPosition;

    [Header("로비 및 맵 설정")]
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

    [ServerRpc(RequireOwnership = false)]
    public void TargetButtonInteractedServerRpc()
    {
        if (!IsServer) return;

        if (!isGamePlaying.Value)
        {
            StartGameRound();
        }
        else
        {
            EvaluateQuotaAndReturn();
        }
    }

    private void StartGameRound()
    {
        if (!IsServer) return;

        Debug.Log($"[시스템] 라운드 {currentRound.Value} 시작! 목표 알 개수: {quotaRequired.Value}개");

        if (houseMapPrefab != null)
        {
            spawnedMap = Instantiate(houseMapPrefab, playMapSpawnPoint.position, Quaternion.identity);
            NetworkObject mapNetObj = spawnedMap.GetComponent<NetworkObject>();
            if (mapNetObj != null)
            {
                mapNetObj.Spawn();
            }

            Physics.SyncTransforms();

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

    private System.Collections.IEnumerator TeleportDelayRoutine()
    {
        yield return new WaitForSeconds(0.15f);

        if (playMapSpawnPoint != null)
        {
            TeleportAllPlayers(playMapSpawnPoint.position + Vector3.up * 1.5f);
        }
        else
        {
            TeleportAllPlayers(new Vector3(0f, 1002f, 0f));
        }
    }

    [ClientRpc]
    private void BuildFurnitureOnClientRpc(int seed)
    {
        if (IsServer) return;

        MapGenerator mapGen = FindFirstObjectByType<MapGenerator>();
        if (mapGen != null)
        {
            mapGen.BuildNewMapFromSeed(seed);
        }
    }

    /// <summary>
    /// ? [새로 추가됨] 파리 대왕이 알을 흡수할 때 서버에서 호출할 함수
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SubmitEggServerRpc()
    {
        if (!IsServer) return;

        eggsSubmittedThisRound.Value += 1;
        Debug.Log($"[파리대왕] 알 흡수 완료! 현재 제출량: {eggsSubmittedThisRound.Value} / 목표: {quotaRequired.Value}");
    }

    /// <summary>
    /// ? [새로 추가됨] 철수 버튼을 눌렀을 때 할당량을 달성했는지 평가하는 함수
    /// </summary>
    private void EvaluateQuotaAndReturn()
    {
        if (!IsServer) return;

        // 성공 조건: 바친 알이 목표치 이상인 경우
        if (eggsSubmittedThisRound.Value >= quotaRequired.Value)
        {
            Debug.Log($"[시스템] 할당량 달성 성공! 바친 알 {eggsSubmittedThisRound.Value}개가 상점 재화로 환전됩니다.");

            // 바친 알 개수만큼 재화 지급
            sharedEggCount.Value += eggsSubmittedThisRound.Value;

            // 다음 라운드로 레벨업 및 할당량 증가 계산
            currentRound.Value += 1;
            quotaRequired.Value = CalculateNextQuota(currentRound.Value);

            // 라운드 제출량 초기화
            eggsSubmittedThisRound.Value = 0;

            // 정상적으로 로비 복귀
            ReturnToLobby();
        }
        else
        {
            // 실패 조건: 할당량을 채우지 못하고 귀환한 경우 -> 게임 오버
            TriggerGameOver();
        }
    }

    /// <summary>
    /// ? [새로 추가됨] 라운드가 증가할 때마다 할당량을 점진적으로 올리는 공식
    /// </summary>
    private int CalculateNextQuota(int round)
    {
        // 1라운드: 10개, 2라운드: 22개, 3라운드: 35개... (점점 무거워지는 밸런스)
        return 3 + (round - 1) * 5 + (int)(round * 2.5f);
    }

    /// <summary>
    /// ? [새로 추가됨] 할당량 미달 시 런을 강제 종료하고 리셋하는 게임 오버 시스템
    /// </summary>
    private void TriggerGameOver()
    {
        Debug.LogError($"[게임 오버] 파리 대왕을 만족시키지 못했습니다! 최종 라운드: {currentRound.Value}");

        // 모든 진행 데이터 완전 초기화
        currentRound.Value = 1;
        quotaRequired.Value = 3;
        eggsSubmittedThisRound.Value = 0;
        sharedEggCount.Value = 0; // 초기 자금으로 리셋

        // 강제 청소 및 로비 사지(쉘터)로 사출
        ReturnToLobby();
    }

    private void ReturnToLobby()
    {
        if (!IsServer) return;

        if (lobbySpawnPoint != null) TeleportAllPlayers(lobbySpawnPoint.position + Vector3.up * 1f);
        else TeleportAllPlayers(new Vector3(0f, 1.5f, 0f));

        CleanUpAllSpawnedObjects();

        if (spawnedMap != null)
        {
            NetworkObject mapNetObj = spawnedMap.GetComponent<NetworkObject>();
            if (mapNetObj != null && mapNetObj.IsSpawned)
            {
                mapNetObj.Despawn();
            }
            Destroy(spawnedMap);
        }

        isGamePlaying.Value = false;
    }

    private void CleanUpAllSpawnedObjects()
    {
        if (!IsServer) return;

        CleanUpTags("Food");
        CleanUpTags("Enemy");
        CleanUpTags("Cobweb");
        CleanUpTags("Egg"); // ? 들고 오지 못하고 던져진 필드의 알들도 다음 판을 위해 깔끔히 청소
    }

    private void CleanUpTags(string tagName)
    {
        GameObject[] objects = GameObject.FindGameObjectsWithTag(tagName);
        foreach (GameObject obj in objects)
        {
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn();
            }
            else
            {
                Destroy(obj);
            }
        }
    }

    private void TeleportAllPlayers(Vector3 targetPosition)
    {
        PlayerMovement[] allPlayers = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        foreach (PlayerMovement player in allPlayers)
        {
            player.TeleportClientRpc(targetPosition);
        }
    }
}