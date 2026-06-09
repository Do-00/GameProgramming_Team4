using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;

    [Header("UI 연동")]
    // ? [새로 추가됨] 인스펙터에서 GameOverManager를 드래그해서 연결해주세요.
    [SerializeField] private GameOverManager gameOverManager;

    [Header("팀원 공유 데이터")]
    public NetworkVariable<int> sharedEggCount = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("할당량(Quota) 시스템 설정")]
    public NetworkVariable<int> currentRound = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> quotaRequired = new NetworkVariable<int>(
        10, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
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

    private IEnumerator TeleportDelayRoutine()
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

    [ServerRpc(RequireOwnership = false)]
    public void SubmitEggServerRpc()
    {
        if (!IsServer) return;

        eggsSubmittedThisRound.Value += 1;
        Debug.Log($"[파리대왕] 알 흡수 완료! 현재 제출량: {eggsSubmittedThisRound.Value} / 목표: {quotaRequired.Value}");
    }

    private void EvaluateQuotaAndReturn()
    {
        if (!IsServer) return;

        if (eggsSubmittedThisRound.Value >= quotaRequired.Value)
        {
            Debug.Log($"[시스템] 할당량 달성 성공! 바친 알 {eggsSubmittedThisRound.Value}개가 상점 재화로 환전됩니다.");

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

    private int CalculateNextQuota(int round)
    {
        return 3 + (round - 1) * 5 + (int)(round * 2.5f);
    }

    public void TriggerGameOver()
    {
        Debug.Log($"[게임 오버] 파리 대왕을 만족시키지 못했습니다! (또는 전멸) 최종 라운드: {currentRound.Value}");

        currentRound.Value = 1;
        quotaRequired.Value = 3;
        eggsSubmittedThisRound.Value = 0;
        sharedEggCount.Value = 0;

        // ? [수정됨] UI 매니저 호출 및 로비 귀환 지연
        if (gameOverManager != null)
        {
            // UI를 띄우고 이미지를 넘깁니다.
            gameOverManager.TriggerGameOverUI();

            // 이미지 3장 x 3초 = 9초이므로, 10초 뒤에 로비로 텔레포트 시킵니다.
            StartCoroutine(DelayedReturnToLobby(10f));
        }
        else
        {
            // UI가 연결 안 되어 있으면 바로 귀환합니다.
            ReturnToLobby();
        }
    }

    // ? [새로 추가됨] 일정 시간 대기 후 로비로 귀환하는 코루틴
    private IEnumerator DelayedReturnToLobby(float delay)
    {
        yield return new WaitForSeconds(delay);
        ReturnToLobby();

        // 텔레포트 완료 후 게임 오버 캔버스를 닫아줍니다.
        if (gameOverManager != null)
        {
            gameOverManager.HideGameOverUIClientRpc();
        }
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
        CleanUpTags("Egg");
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
            if (player.isDead.Value)
            {
                player.ReviveServerRpc(targetPosition);
            }
            else
            {
                player.TeleportClientRpc(targetPosition);
            }
        }
    }
}