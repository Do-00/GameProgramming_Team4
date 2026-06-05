using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class MapGenerator : NetworkBehaviour
{
    public enum RoomType { LivingRoom, NormalRoom, Kitchen, Bathroom }

    [System.Serializable]
    public struct RoomPrefabData
    {
        public GameObject prefab;
        public Vector2Int size;
        public RoomType type;

        [Header("이 방 전용 가구 설정")]
        public List<FurnitureData> wallPool;
        public List<FurnitureData> centerPool;
    }

    [System.Serializable]
    public struct FurnitureData
    {
        public GameObject prefab;
        [Range(0f, 1f)] public float spawnChance;
        public bool isUnique;
    }

    [System.Serializable]
    public struct ItemData
    {
        public GameObject prefab;
        [Range(0f, 1f)] public float spawnChance;
    }

    // ? [새로 추가됨] 적 생성용 데이터 구조체
    [System.Serializable]
    public struct EnemyData
    {
        public GameObject prefab; // ?? 반드시 NetworkObject가 붙어있어야 합니다!
        [Range(0f, 1f)] public float spawnChance; // 일반 방 스폰 확률
    }

    private class RoomInstance
    {
        public GameObject roomObject;
        public Vector2Int originGrid;
        public RoomType roomType;
        public Vector2Int size;
        public List<FurnitureData> assignedWallPool;
        public List<FurnitureData> assignedCenterPool;
    }

    [Header("프리팹 데이터 베이스")]
    [SerializeField] private GameObject livingRoomPrefab;
    [SerializeField] private GameObject bathroomPrefab;
    [SerializeField] private GameObject doorPrefab;

    [Header("일반 방(Variants) 종합 설정")]
    [SerializeField] private List<RoomPrefabData> normalRoomVariants = new List<RoomPrefabData>();

    [Header("★ 거실(LivingRoom) 전용 가구 설정")]
    [SerializeField] private List<FurnitureData> livingWallPool = new List<FurnitureData>();
    [SerializeField] private List<FurnitureData> livingCenterPool = new List<FurnitureData>();

    [Header("★ 화장실(Bathroom) 전용 가구 설정")]
    [SerializeField] private List<FurnitureData> bathroomWallPool = new List<FurnitureData>();
    [SerializeField] private List<FurnitureData> bathroomCenterPool = new List<FurnitureData>();

    [Header("★ 음식/아이템(Food) 스폰 설정")]
    [SerializeField] private List<ItemData> foodItemPool = new List<ItemData>();

    // ? [새로 추가됨] 인스펙터 적 프리팹 설정 주머니
    [Header("★ 적(Enemy) 스폰 설정")]
    [SerializeField] private GameObject catPrefab; // 거실 고정 고양이 프리팹
    [SerializeField] private EnemyData spiderData; // 일반 방 확률 스폰 거미 데이터

    [Header("충돌 감지 설정")]
    [SerializeField] private LayerMask furnitureLayer;
    [SerializeField] private LayerMask itemSpawnRaycastMask;

    [Header("★ 음식 스폰 밸런스 옵션")]
    [SerializeField] private int minItemsPerUnit = 1;
    [SerializeField] private int maxItemsPerUnit = 3;

    [Header("설정 수치")]
    [SerializeField] private float unitSize = 50f;
    [SerializeField] private int maxDepth = 3;

    [Header("방 개수 밸런스 설정")]
    [SerializeField] private int minRooms = 3;
    [SerializeField] private int maxRooms = 6;

    [Header("문 기본 회전 설정 (소켓 없을 때)")]
    [SerializeField] private Vector3 fallbackDoorRotationNS = new Vector3(0f, 90f, 0f);
    [SerializeField] private Vector3 fallbackDoorRotationEW = new Vector3(0f, 0f, 0f);

    private Dictionary<Vector2Int, RoomInstance> roomGridMap = new Dictionary<Vector2Int, RoomInstance>();
    private HashSet<string> treeEdges = new HashSet<string>();

    private int totalRoomCount = 0;
    private int bathroomCount = 0;
    private int maxBathrooms = 1;
    private int targetRoomCount = 0;

    private List<GameObject> spawnedRoomObjects = new List<GameObject>();

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            int randomSeed = Random.Range(1, 100000);
            StartCoroutine(GenerateMapNextFrame(randomSeed));
            GenerateMapClientRpc(randomSeed);
        }
    }

    [ClientRpc]
    private void GenerateMapClientRpc(int seed)
    {
        if (IsServer) return;
        StartCoroutine(GenerateMapNextFrame(seed));
    }

    private IEnumerator GenerateMapNextFrame(int seed)
    {
        yield return null;
        GenerateValidHouse(seed);
    }

    private void GenerateValidHouse(int seed)
    {
        Random.InitState(seed);

        int attempts = 0;
        while (attempts < 20)
        {
            attempts++;
            ClearCurrentMap();

            targetRoomCount = Random.Range(minRooms, maxRooms + 1);
            BuildHouse();

            if (totalRoomCount == targetRoomCount)
            {
                CarveWallsAndSpawnDoors();
                SpawnFurnitureInRooms();

                // 가구 배치가 완전히 동기화되어 고정된 후 물체들을 배치합니다.
                SpawnFoodItemsWithRaycast();

                // ? [새로 추가됨] 적 생성 함수 호출
                SpawnEnemiesWithRaycast();

                Debug.Log($"[MapGenerator] 시드({seed}) 생성 성공! 오브젝트 총합: {spawnedRoomObjects.Count}개");

                if (IsServer)
                {
                    foreach (GameObject obj in spawnedRoomObjects)
                    {
                        if (obj == null) continue;
                        NetworkObject netObj = obj.GetComponent<NetworkObject>();
                        if (netObj != null) netObj.Spawn();
                    }
                }
                break;
            }
        }
    }

    private void ClearCurrentMap()
    {
        foreach (GameObject obj in spawnedRoomObjects)
        {
            if (obj != null) Destroy(obj);
        }
        spawnedRoomObjects.Clear();
        roomGridMap.Clear();
        treeEdges.Clear();

        totalRoomCount = 0;
        bathroomCount = 0;
    }

    private string GetEdgeKey(Vector2Int a, Vector2Int b)
    {
        return a.x < b.x || (a.x == b.x && a.y < b.y) ? $"{a.x},{a.y}_{b.x},{b.y}" : $"{b.x},{b.y}_{a.x},{a.y}";
    }

    private void BuildHouse()
    {
        maxBathrooms = Random.Range(1, 3);

        Vector3 livingRoomPos = new Vector3(unitSize / 2f, 0f, unitSize / 2f);
        GameObject lrObj = InstantiateRoomObject(livingRoomPrefab, livingRoomPos);

        RoomInstance lrInstance = new RoomInstance
        {
            roomObject = lrObj,
            originGrid = new Vector2Int(0, 0),
            roomType = RoomType.LivingRoom,
            size = new Vector2Int(2, 2),
            assignedWallPool = livingWallPool,
            assignedCenterPool = livingCenterPool
        };
        roomGridMap.Add(new Vector2Int(0, 0), lrInstance);
        roomGridMap.Add(new Vector2Int(1, 0), lrInstance);
        roomGridMap.Add(new Vector2Int(0, 1), lrInstance);
        roomGridMap.Add(new Vector2Int(1, 1), lrInstance);

        roomGridMap.Add(new Vector2Int(0, 2), null);
        roomGridMap.Add(new Vector2Int(1, 2), null);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.startRoomPosition = livingRoomPos;
        }

        TrySpawnRoomTree(new Vector2Int(0, -1), 1);
        treeEdges.Add(GetEdgeKey(new Vector2Int(0, -1), new Vector2Int(0, 0)));

        TrySpawnRoomTree(new Vector2Int(2, 0), 1);
        treeEdges.Add(GetEdgeKey(new Vector2Int(2, 0), new Vector2Int(1, 0)));

        TrySpawnRoomTree(new Vector2Int(-1, 0), 1);
        treeEdges.Add(GetEdgeKey(new Vector2Int(-1, 0), new Vector2Int(0, 0)));
    }

    private void TrySpawnRoomTree(Vector2Int gridPos, int currentDepth)
    {
        if (totalRoomCount >= targetRoomCount) return;
        if (gridPos.y >= 2) return;
        if (currentDepth > maxDepth || roomGridMap.ContainsKey(gridPos)) return;

        RoomType assignedType = RoomType.NormalRoom;
        if (bathroomCount < maxBathrooms)
        {
            if (totalRoomCount == targetRoomCount - 1 || currentDepth == maxDepth || (currentDepth > 1 && Random.value < 0.3f))
            {
                assignedType = RoomType.Bathroom;
            }
        }

        GameObject selectedPrefab = null;
        Vector2Int roomSize = new Vector2Int(1, 1);
        RoomPrefabData chosenData = default;

        if (assignedType == RoomType.Bathroom)
        {
            selectedPrefab = bathroomPrefab;
        }
        else
        {
            List<RoomPrefabData> shuffledVariants = new List<RoomPrefabData>(normalRoomVariants);
            for (int i = shuffledVariants.Count - 1; i > 0; i--)
            {
                int r = Random.Range(0, i + 1);
                var temp = shuffledVariants[i]; shuffledVariants[i] = shuffledVariants[r]; shuffledVariants[r] = temp;
            }

            foreach (var variant in shuffledVariants)
            {
                if (CanFitRoom(gridPos, variant.size))
                {
                    selectedPrefab = variant.prefab;
                    roomSize = variant.size;
                    chosenData = variant;
                    break;
                }
            }

            if (selectedPrefab == null && normalRoomVariants.Count > 0)
            {
                chosenData = normalRoomVariants[0];
                selectedPrefab = chosenData.prefab;
                roomSize = new Vector2Int(1, 1);
            }
        }

        if (selectedPrefab == null) return;

        Vector3 worldPos = new Vector3(
            gridPos.x * unitSize + (roomSize.x - 1) * unitSize / 2f,
            0f,
            gridPos.y * unitSize + (roomSize.y - 1) * unitSize / 2f
        );

        GameObject roomObj = InstantiateRoomObject(selectedPrefab, worldPos);

        RoomInstance roomInstance = new RoomInstance
        {
            roomObject = roomObj,
            originGrid = gridPos,
            roomType = assignedType,
            size = roomSize,
            assignedWallPool = (assignedType == RoomType.Bathroom) ? bathroomWallPool : chosenData.wallPool,
            assignedCenterPool = (assignedType == RoomType.Bathroom) ? bathroomCenterPool : chosenData.centerPool
        };

        for (int x = 0; x < roomSize.x; x++)
        {
            for (int y = 0; y < roomSize.y; y++)
            {
                Vector2Int occupyGrid = gridPos + new Vector2Int(x, y);
                if (!roomGridMap.ContainsKey(occupyGrid))
                {
                    roomGridMap.Add(occupyGrid, roomInstance);
                }
            }
        }

        totalRoomCount++;

        if (assignedType == RoomType.Bathroom)
        {
            bathroomCount++;
            return;
        }

        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        ShuffleArray(directions);

        foreach (Vector2Int dir in directions)
        {
            Vector2Int edgeGrid = gridPos;
            if (dir == Vector2Int.up) edgeGrid += new Vector2Int(0, roomSize.y - 1);
            if (dir == Vector2Int.right) edgeGrid += new Vector2Int(roomSize.x - 1, 0);

            Vector2Int nextGrid = edgeGrid + dir;
            if (!roomGridMap.ContainsKey(nextGrid))
            {
                treeEdges.Add(GetEdgeKey(edgeGrid, nextGrid));
                TrySpawnRoomTree(nextGrid, currentDepth + 1);
            }
        }
    }

    private bool CanFitRoom(Vector2Int startGrid, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector2Int checkGrid = startGrid + new Vector2Int(x, y);
                if (checkGrid.y >= 2) return false;
                if (roomGridMap.ContainsKey(checkGrid)) return false;
            }
        }
        return true;
    }

    private void CarveWallsAndSpawnDoors()
    {
        HashSet<string> processedEdges = new HashSet<string>();

        foreach (KeyValuePair<Vector2Int, RoomInstance> cell in roomGridMap)
        {
            Vector2Int currentGrid = cell.Key;
            RoomInstance currentRoomInstance = cell.Value;
            if (currentRoomInstance == null) continue;

            GameObject currentRoom = currentRoomInstance.roomObject;
            Vector2Int localGrid = currentGrid - currentRoomInstance.originGrid;
            Vector2Int[] scanDirs = { Vector2Int.up, Vector2Int.right };

            foreach (Vector2Int dir in scanDirs)
            {
                Vector2Int neighborGrid = currentGrid + dir;
                if (roomGridMap.ContainsKey(neighborGrid) && roomGridMap[neighborGrid] != null)
                {
                    RoomInstance neighborInstance = roomGridMap[neighborGrid];
                    GameObject neighborRoom = neighborInstance.roomObject;

                    if (currentRoom != neighborRoom)
                    {
                        string edgeKey = GetEdgeKey(currentGrid, neighborGrid);
                        if (processedEdges.Contains(edgeKey)) continue;
                        processedEdges.Add(edgeKey);

                        if (!treeEdges.Contains(edgeKey)) continue;

                        if (dir == Vector2Int.up)
                        {
                            string detailedWallNorth = $"Wall_North_{localGrid.x}_{localGrid.y}";
                            Transform wallNorth = FindChildRecursive(currentRoom.transform, detailedWallNorth);
                            if (wallNorth == null) wallNorth = FindChildRecursive(currentRoom.transform, "Wall_North");
                            if (wallNorth != null) wallNorth.gameObject.SetActive(false);

                            Vector2Int northLocalGrid = neighborGrid - neighborInstance.originGrid;
                            string detailedWallSouth = $"Wall_South_{northLocalGrid.x}_{northLocalGrid.y}";
                            Transform wallSouth = FindChildRecursive(neighborRoom.transform, detailedWallSouth);
                            if (wallSouth == null) wallSouth = FindChildRecursive(neighborRoom.transform, "Wall_South");
                            if (wallSouth != null) wallSouth.gameObject.SetActive(false);

                            if (IsServer)
                            {
                                string detailedSocketName = $"Socket_North_{localGrid.x}_{localGrid.y}";
                                Transform socketTransform = FindChildRecursive(currentRoom.transform, detailedSocketName);
                                if (socketTransform == null) socketTransform = FindChildRecursive(currentRoom.transform, "Socket_North");

                                if (socketTransform != null) InstantiateRoomObject(doorPrefab, socketTransform.position, socketTransform.rotation);
                                else
                                {
                                    Vector3 doorPos = new Vector3(currentGrid.x * unitSize, 0f, currentGrid.y * unitSize) + new Vector3(0f, 0f, unitSize / 2f);
                                    InstantiateRoomObject(doorPrefab, doorPos, Quaternion.Euler(fallbackDoorRotationNS));
                                }
                            }
                        }
                        else if (dir == Vector2Int.right)
                        {
                            string detailedWallEast = $"Wall_East_{localGrid.x}_{localGrid.y}";
                            Transform wallEast = FindChildRecursive(currentRoom.transform, detailedWallEast);
                            if (wallEast == null) wallEast = FindChildRecursive(currentRoom.transform, "Wall_East");
                            if (wallEast != null) wallEast.gameObject.SetActive(false);

                            Vector2Int eastLocalGrid = neighborGrid - neighborInstance.originGrid;
                            string detailedWallWest = $"Wall_West_{eastLocalGrid.x}_{eastLocalGrid.y}";
                            Transform wallWest = FindChildRecursive(neighborRoom.transform, detailedWallWest);
                            if (wallWest == null) wallWest = FindChildRecursive(neighborRoom.transform, "Wall_West");
                            if (wallWest != null) wallWest.gameObject.SetActive(false);

                            if (IsServer)
                            {
                                string detailedSocketName = $"Socket_East_{localGrid.x}_{localGrid.y}";
                                Transform socketTransform = FindChildRecursive(currentRoom.transform, detailedSocketName);
                                if (socketTransform == null) socketTransform = FindChildRecursive(currentRoom.transform, "Socket_East");

                                if (socketTransform != null) InstantiateRoomObject(doorPrefab, socketTransform.position, socketTransform.rotation);
                                else
                                {
                                    Vector3 doorPos = new Vector3(currentGrid.x * unitSize, 0f, currentGrid.y * unitSize) + new Vector3(unitSize / 2f, 0f, 0f);
                                    InstantiateRoomObject(doorPrefab, doorPos, Quaternion.Euler(fallbackDoorRotationEW));
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private void SpawnFurnitureInRooms()
    {
        List<GameObject> checkedRooms = new List<GameObject>();

        foreach (KeyValuePair<Vector2Int, RoomInstance> cell in roomGridMap)
        {
            if (cell.Value == null || checkedRooms.Contains(cell.Value.roomObject)) continue;
            GameObject roomObj = cell.Value.roomObject;
            checkedRooms.Add(roomObj);

            HashSet<GameObject> spawnedUniquePrefabs = new HashSet<GameObject>();
            Transform[] allChildren = roomObj.GetComponentsInChildren<Transform>(false);

            foreach (Transform child in allChildren)
            {
                if (child.name.StartsWith("Socket_Furniture"))
                {
                    List<FurnitureData> selectedPool = null;

                    if (child.name.Contains("_Wall"))
                    {
                        selectedPool = cell.Value.assignedWallPool;
                    }
                    else if (child.name.Contains("_Center"))
                    {
                        selectedPool = cell.Value.assignedCenterPool;
                    }

                    if (selectedPool == null || selectedPool.Count == 0) continue;

                    List<FurnitureData> availableFurniture = new List<FurnitureData>();
                    foreach (var furniture in selectedPool)
                    {
                        if (furniture.isUnique && spawnedUniquePrefabs.Contains(furniture.prefab))
                        {
                            continue;
                        }
                        availableFurniture.Add(furniture);
                    }

                    if (availableFurniture.Count == 0) continue;

                    for (int i = availableFurniture.Count - 1; i > 0; i--)
                    {
                        int r = Random.Range(0, i + 1);
                        var temp = availableFurniture[i]; availableFurniture[i] = availableFurniture[r]; availableFurniture[r] = temp;
                    }

                    foreach (var furniture in availableFurniture)
                    {
                        if (Random.value <= furniture.spawnChance)
                        {
                            if (furniture.prefab == null) continue;

                            BoxCollider boxCol = furniture.prefab.GetComponentInChildren<BoxCollider>();
                            if (boxCol != null)
                            {
                                Vector3 localCenter = boxCol.transform.localPosition + boxCol.center;
                                Vector3 worldCenter = child.position + (child.rotation * localCenter);

                                Vector3 halfExtents = boxCol.size * 0.5f;
                                halfExtents *= 0.95f;

                                Quaternion worldRotation = child.rotation * boxCol.transform.localRotation;

                                Collider[] hitColliders = Physics.OverlapBox(worldCenter, halfExtents, worldRotation, furnitureLayer);

                                if (hitColliders.Length > 0) continue;
                            }

                            GameObject furnitureObj = Instantiate(furniture.prefab, child.position, child.rotation);
                            furnitureObj.transform.SetParent(roomObj.transform);

                            Physics.SyncTransforms();

                            if (furniture.isUnique)
                            {
                                spawnedUniquePrefabs.Add(furniture.prefab);
                            }
                            break;
                        }
                    }
                }
            }
        }
    }

    private void SpawnFoodItemsWithRaycast()
    {
        if (!IsServer) return;
        if (foodItemPool.Count == 0) return;

        List<GameObject> checkedRooms = new List<GameObject>();

        foreach (KeyValuePair<Vector2Int, RoomInstance> cell in roomGridMap)
        {
            if (cell.Value == null || checkedRooms.Contains(cell.Value.roomObject)) continue;
            RoomInstance currentRoom = cell.Value;
            checkedRooms.Add(currentRoom.roomObject);

            int totalCells = currentRoom.size.x * currentRoom.size.y;
            int itemSpawnAttempts = Random.Range(minItemsPerUnit, maxItemsPerUnit + 1) * totalCells;

            Vector3 roomCenterWorldPos = currentRoom.roomObject.transform.position;

            float totalWorldWidth = currentRoom.size.x * unitSize;
            float totalWorldLength = currentRoom.size.y * unitSize;

            float halfWidth = totalWorldWidth * 0.5f;
            float halfLength = totalWorldLength * 0.5f;

            float minX = roomCenterWorldPos.x - halfWidth;
            float maxX = roomCenterWorldPos.x + halfWidth;
            float minZ = roomCenterWorldPos.z - halfLength;
            float maxZ = roomCenterWorldPos.z + halfLength;

            minX += 2.5f; maxX -= 2.5f;
            minZ += 2.5f; maxZ -= 2.5f;

            for (int i = 0; i < itemSpawnAttempts; i++)
            {
                ItemData selectedItem = foodItemPool[Random.Range(0, foodItemPool.Count)];

                if (Random.value <= selectedItem.spawnChance)
                {
                    if (selectedItem.prefab == null) continue;

                    float randomX = Random.Range(minX, maxX);
                    float randomZ = Random.Range(minZ, maxZ);

                    Vector3 rayOrigin = new Vector3(randomX, 20f, randomZ);

                    RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, 40f, itemSpawnRaycastMask);

                    if (hits.Length > 0)
                    {
                        Vector3 highestPoint = hits[0].point;
                        float highestY = hits[0].point.y;

                        for (int j = 1; j < hits.Length; j++)
                        {
                            if (hits[j].point.y > highestY)
                            {
                                highestY = hits[j].point.y;
                                highestPoint = hits[j].point;
                            }
                        }

                        Vector3 spawnPosition = highestPoint;

                        GameObject itemObj = Instantiate(selectedItem.prefab, spawnPosition, Quaternion.identity);
                        NetworkObject netObj = itemObj.GetComponent<NetworkObject>();
                        if (netObj != null)
                        {
                            netObj.Spawn();
                        }
                    }
                }
            }
        }
    }

    // ? [새로 추가됨] 조건형 스마트 적(Enemy) 레이저 드롭 엔진
    private void SpawnEnemiesWithRaycast()
    {
        if (!IsServer) return; // 멀티플레이 스폰 권한 방어

        List<GameObject> checkedRooms = new List<GameObject>();
        List<RoomInstance> nonLivingRooms = new List<RoomInstance>();

        int spawnedSpiderCount = 0; // 이번 판에 태어난 총 거미 카운트 변수

        // 1단계: 맵 전역 순회하며 확정 구역(거실) 연산 진행 및 일반 방 후보 수집
        foreach (KeyValuePair<Vector2Int, RoomInstance> cell in roomGridMap)
        {
            if (cell.Value == null || checkedRooms.Contains(cell.Value.roomObject)) continue;
            RoomInstance currentRoom = cell.Value;
            checkedRooms.Add(currentRoom.roomObject);

            // ?? 규칙 1: 거실이라면 주사인 굴리지 않고 무조건 고양이 1마리 확정 스폰!
            if (currentRoom.roomType == RoomType.LivingRoom)
            {
                if (catPrefab != null)
                {
                    SpawnEnemyInRoomBounds(currentRoom, catPrefab);
                }
            }
            else
            {
                // 거실이 아닌 일반 방, 화장실 등은 2단계 연산을 위해 따로 리스트에 수집
                nonLivingRooms.Add(currentRoom);
            }
        }

        // 2단계: 수집된 일반 방들을 돌며 설정된 확률로 거미 스폰 진행
        if (spiderData.prefab != null && nonLivingRooms.Count > 0)
        {
            foreach (RoomInstance room in nonLivingRooms)
            {
                if (Random.value <= spiderData.spawnChance)
                {
                    bool success = SpawnEnemyInRoomBounds(room, spiderData.prefab);
                    if (success) spawnedSpiderCount++;
                }
            }

            // ?? 규칙 2: 맵 전체를 다 돌았는데 운이 나빠 거미가 0마리라면?
            // "최소 한 마리는 무조건 생성" 규칙을 위해 수집된 일반 방 중 랜덤으로 1곳을 골라 강제 소환!
            if (spawnedSpiderCount == 0 && nonLivingRooms.Count > 0)
            {
                RoomInstance fallbackRoom = nonLivingRooms[Random.Range(0, nonLivingRooms.Count)];
                SpawnEnemyInRoomBounds(fallbackRoom, spiderData.prefab);
                Debug.Log($"[MapGenerator] 거미가 확률을 못 뚫어 {fallbackRoom.roomObject.name}에 최소 보장 거미 1마리를 강제 생성했습니다.");
            }
        }
    }

    // ? [적 스폰용 헬퍼 함수] 지정된 방 면적 내에 안전하게 탑다운 레이저로 적을 떨어뜨립니다.
    private bool SpawnEnemyInRoomBounds(RoomInstance room, GameObject enemyPrefab)
    {
        Vector3 roomCenter = room.roomObject.transform.position;
        float halfWidth = (room.size.x * unitSize) * 0.5f;
        float halfLength = (room.size.y * unitSize) * 0.5f;

        // 벽에 끼지 않도록 패딩 세팅
        float minX = roomCenter.x - halfWidth + 3.0f;
        float maxX = roomCenter.x + halfWidth - 3.0f;
        float minZ = roomCenter.z - halfLength + 3.0f;
        float maxZ = roomCenter.z + halfLength - 3.0f;

        float randomX = Random.Range(minX, maxX);
        float randomZ = Random.Range(minZ, maxZ);

        Vector3 rayOrigin = new Vector3(randomX, 20f, randomZ);
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, 40f, itemSpawnRaycastMask);

        if (hits.Length > 0)
        {
            Vector3 highestPoint = hits[0].point;
            float highestY = hits[0].point.y;

            for (int j = 1; j < hits.Length; j++)
            {
                if (hits[j].point.y > highestY)
                {
                    highestY = hits[j].point.y;
                    highestPoint = hits[j].point;
                }
            }

            // 안전한 착지점 좌표 획득 (가구 위 혹은 바닥)
            Vector3 spawnPosition = highestPoint;

            GameObject enemyObj = Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
            NetworkObject netObj = enemyObj.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
            }
            return true; // 스폰 성공 반환
        }
        return false; // 스폰 실패 반환
    }

    private Transform FindChildRecursive(Transform parent, string targetName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == targetName) return child;
            Transform found = FindChildRecursive(child, targetName);
            if (found != null) return found;
        }
        return null;
    }

    private GameObject InstantiateRoomObject(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;
        GameObject obj = Instantiate(prefab, position, rotation);
        spawnedRoomObjects.Add(obj);
        return obj;
    }

    private GameObject InstantiateRoomObject(GameObject prefab, Vector3 position)
    {
        return InstantiateRoomObject(prefab, position, Quaternion.identity);
    }

    private void ShuffleArray(Vector2Int[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int rnd = Random.Range(0, i + 1);
            Vector2Int temp = array[i];
            array[i] = array[rnd];
            array[rnd] = temp;
        }
    }
}