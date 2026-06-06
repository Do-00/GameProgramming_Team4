using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;

    [Header("팀원 공유 데이터")]
    // 팀원들이 추가한 공유 알 개수 (서버만 쓰기 가능, 모두 읽기 가능)
    public NetworkVariable<int> sharedEggCount = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [HideInInspector]
    public Vector3 startRoomPosition; // MapGenerator가 연산을 끝내고 대입해 줄 실제 집 거실의 좌표

    [Header("로비 및 맵 설정")]
    [SerializeField] private GameObject houseMapPrefab; // 생성 및 파괴할 집 맵 프리팹 (NetworkObject 필수)
    [SerializeField] private Transform lobbySpawnPoint;   // 처음 진입하는 로비(쉘터) 스폰 위치
    [SerializeField] private Transform playMapSpawnPoint; // 실제 집 맵 프리팹이 생성될 월드 중심 위치

    private GameObject spawnedMap; // 관리용 복제본 맵 저장 변수
    private NetworkVariable<bool> isGamePlaying = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        // 싱글톤 패턴 유지
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// [팀원 코드 통합] 아무나 버튼을 눌러 게임을 시작하려고 할 때 호출되는 서버 RPC
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void StartGameServerRpc()
    {
        if (!IsServer) return;

        // 이미 게임이 진행 중이 아니라면 라운드 시작
        if (!isGamePlaying.Value)
        {
            StartGameRound();
        }
        else
        {
            Debug.LogWarning("[시스템] 이미 게임이 진행 중입니다!");
        }
    }

    /// <summary>
    /// 타겟 버튼 상호작용 (게임 시작 또는 로비 복귀 토글)
    /// </summary>
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
            ReturnToLobby();
        }
    }

    private void StartGameRound()
    {
        if (!IsServer) return;

        Debug.Log("[시스템] 게임 시작! 맵 동적 빌드를 시작합니다.");

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

                Debug.Log($"[시스템] 시드 {masterSeed} 주입 성공! 인테리어 배치를 시작합니다.");
            }
            else
            {
                Debug.LogError("[에러] 월드에서 MapGenerator 컴포넌트를 찾지 못했습니다! 프리팹을 확인하세요.");
            }
        }

        StartCoroutine(TeleportDelayRoutine());
        isGamePlaying.Value = true;
    }

    private System.Collections.IEnumerator TeleportDelayRoutine()
    {
        yield return new WaitForSeconds(0.15f); // 맵 지형과 콜라이더가 정렬될 시간 대기

        if (playMapSpawnPoint != null)
        {
            // 지정된 Play Map Spawn Point로 플레이어 전원 이동
            TeleportAllPlayers(playMapSpawnPoint.position + Vector3.up * 1.5f);
            Debug.Log("[시스템] 플레이어들을 지정된 Play Map Spawn Point로 정상 이동시켰습니다.");
        }
        else
        {
            TeleportAllPlayers(new Vector3(0f, 1002f, 0f));
            Debug.LogError("[에러] GameManager의 Play Map Spawn Point가 비어있습니다!");
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
    /// [서버 권한] 철수 버튼 클릭 시 실행: 플레이어 대피 후 맵, 음식, 적, 거미줄까지 올클리어 청소
    /// </summary>
    private void ReturnToLobby()
    {
        if (!IsServer) return;

        Debug.Log("[시스템] 작전 종료! 모든 파리를 본부 로비로 철수시키고 지형 및 잔해물 청소를 시작합니다.");

        // 1. 모든 플레이어들을 처음 시작했던 로비(쉘터) 스폰 포인트로 원격 이동
        if (lobbySpawnPoint != null) TeleportAllPlayers(lobbySpawnPoint.position + Vector3.up * 1f);
        else TeleportAllPlayers(new Vector3(0f, 1.5f, 0f));

        // 2. 필드에 남겨진 모든 동적 넷코드 오브젝트 청소
        CleanUpAllSpawnedObjects();

        // 3. 생성되어 필드에 남아있던 집 맵 프리팹 파괴
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

        Debug.Log("[시스템] 필드 잔해물(음식, 적, 거미줄) 클리어 완료.");
    }

    // 중복되는 청소 코드를 하나로 묶은 최적화 메서드
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