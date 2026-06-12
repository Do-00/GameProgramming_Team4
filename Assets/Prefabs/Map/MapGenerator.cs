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

    [System.Serializable]
    public struct EnemyData
    {
        public GameObject prefab;
        [Range(0f, 1f)] public float spawnChance;
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

    [Header("고정 방 프리팹")]
    [SerializeField] private GameObject livingRoomPrefab;
    [SerializeField] private GameObject bathroomPrefab;
    [SerializeField] private GameObject doorPrefab;

    [Header("일반 방 변형 목록")]
    [SerializeField] private List<RoomPrefabData> normalRoomVariants = new List<RoomPrefabData>();

    [Header("거실 가구 풀")]
    [SerializeField] private List<FurnitureData> livingWallPool = new List<FurnitureData>();
    [SerializeField] private List<FurnitureData> livingCenterPool = new List<FurnitureData>();

    [Header("화장실 가구 풀")]
    [SerializeField] private List<FurnitureData> bathroomWallPool = new List<FurnitureData>();
    [SerializeField] private List<FurnitureData> bathroomCenterPool = new List<FurnitureData>();

    [Header("음식 아이템 풀")]
    [SerializeField] private List<ItemData> foodItemPool = new List<ItemData>();

    [Header("적 프리팹")]
    [SerializeField] private GameObject catPrefab;
    [SerializeField] private EnemyData spiderData;

    [Header("충돌 판정 레이어")]
    [SerializeField] private LayerMask furnitureLayer;
    [SerializeField] private LayerMask itemSpawnRaycastMask;

    [Header("방당 아이템 스폰 수")]
    [SerializeField] private int minItemsPerUnit = 1;
    [SerializeField] private int maxItemsPerUnit = 3;

    [Header("맵 배치 설정")]
    // 방 하나의 월드 크기 (단위: 유닛). Inspector에서 실제 스케일에 맞게 조정
    [SerializeField] private float unitSize = 200f;
    [SerializeField] private int maxDepth = 3;

    [Header("방 개수 범위")]
    [SerializeField] private int minRooms = 3;
    [SerializeField] private int maxRooms = 6;

    [Header("문 폴백 회전값 (소켓 미검출 시 사용)")]
    [SerializeField] private Vector3 fallbackDoorRotationNS = new Vector3(0f, 90f, 0f);
    [SerializeField] private Vector3 fallbackDoorRotationEW = new Vector3(0f, 0f, 0f);

    private Dictionary<Vector2Int, RoomInstance> roomGridMap = new Dictionary<Vector2Int, RoomInstance>();
    private HashSet<string> treeEdges = new HashSet<string>();

    private int totalRoomCount = 0;
    private int bathroomCount = 0;
    private int maxBathrooms = 1;
    private int targetRoomCount = 0;

    private List<GameObject> spawnedRoomObjects = new List<GameObject>();

    public override void OnNetworkSpawn() { }

    /// <summary>시드 기반으로 새 맵을 생성 (서버 및 클라이언트 공통 호출)</summary>
    public void BuildNewMapFromSeed(int seed)
    {
        StartCoroutine(GenerateMapNextFrame(seed));
    }

    private IEnumerator GenerateMapNextFrame(int seed)
    {
        yield return null;
        GenerateValidHouse(seed);
    }

    [ClientRpc]
    private void GenerateMapClientRpc(int seed)
    {
        if (IsServer) return;
        StartCoroutine(GenerateMapNextFrame(seed));
    }

    private void GenerateValidHouse(int seed)
    {
        Random.InitState(seed);

        for (int attempt = 0; attempt < 20; attempt++)
        {
            ClearCurrentMap();
            targetRoomCount = Random.Range(minRooms, maxRooms + 1);
            BuildHouse();

            if (totalRoomCount != targetRoomCount) continue;

            CarveWallsAndSpawnDoors();
            SpawnFurnitureInRooms();
            SpawnFoodItemsWithRaycast();
            SpawnEnemiesWithRaycast();

            Debug.Log($"[MapGenerator] 시드({seed}) 생성 성공. 오브젝트 수: {spawnedRoomObjects.Count}개");

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
        return a.x < b.x || (a.x == b.x && a.y < b.y)
            ? $"{a.x},{a.y}_{b.x},{b.y}"
            : $"{b.x},{b.y}_{a.x},{a.y}";
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
            GameManager.Instance.startRoomPosition = livingRoomPos;

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
            bool isLastRoom = totalRoomCount == targetRoomCount - 1;
            bool isMaxDepth = currentDepth == maxDepth;
            bool randomChance = currentDepth > 1 && Random.value < 0.3f;

            if (isLastRoom || isMaxDepth || randomChance)
                assignedType = RoomType.Bathroom;
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
            List<RoomPrefabData> shuffled = new List<RoomPrefabData>(normalRoomVariants);
            ShuffleList(shuffled);

            foreach (var variant in shuffled)
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
            gridPos.y * unitSize + (roomSize.y - 1) * unitSize / 2f);

        GameObject roomObj = InstantiateRoomObject(selectedPrefab, worldPos);

        RoomInstance roomInstance = new RoomInstance
        {
            roomObject = roomObj,
            originGrid = gridPos,
            roomType = assignedType,
            size = roomSize,
            assignedWallPool = assignedType == RoomType.Bathroom ? bathroomWallPool : chosenData.wallPool,
            assignedCenterPool = assignedType == RoomType.Bathroom ? bathroomCenterPool : chosenData.centerPool
        };

        for (int x = 0; x < roomSize.x; x++)
        {
            for (int y = 0; y < roomSize.y; y++)
            {
                Vector2Int cell = gridPos + new Vector2Int(x, y);
                if (!roomGridMap.ContainsKey(cell)) roomGridMap.Add(cell, roomInstance);
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
                Vector2Int cell = startGrid + new Vector2Int(x, y);
                if (cell.y >= 2 || roomGridMap.ContainsKey(cell)) return false;
            }
        }
        return true;
    }

    private void CarveWallsAndSpawnDoors()
    {
        HashSet<string> processedEdges = new HashSet<string>();

        foreach (var cell in roomGridMap)
        {
            Vector2Int currentGrid = cell.Key;
            RoomInstance currentRoom = cell.Value;
            if (currentRoom == null) continue;

            Vector2Int localGrid = currentGrid - currentRoom.originGrid;
            Vector2Int[] scanDirs = { Vector2Int.up, Vector2Int.right };

            foreach (Vector2Int dir in scanDirs)
            {
                Vector2Int neighborGrid = currentGrid + dir;
                if (!roomGridMap.TryGetValue(neighborGrid, out RoomInstance neighborRoom) || neighborRoom == null) continue;
                if (currentRoom.roomObject == neighborRoom.roomObject) continue;

                string edgeKey = GetEdgeKey(currentGrid, neighborGrid);
                if (!processedEdges.Add(edgeKey)) continue;
                if (!treeEdges.Contains(edgeKey)) continue;

                if (dir == Vector2Int.up)
                {
                    DisableWall(currentRoom.roomObject, $"Wall_North_{localGrid.x}_{localGrid.y}", "Wall_North");

                    Vector2Int northLocal = neighborGrid - neighborRoom.originGrid;
                    DisableWall(neighborRoom.roomObject, $"Wall_South_{northLocal.x}_{northLocal.y}", "Wall_South");

                    if (IsServer)
                        SpawnDoor(currentRoom.roomObject, $"Socket_North_{localGrid.x}_{localGrid.y}", "Socket_North",
                            new Vector3(currentGrid.x * unitSize, 0f, currentGrid.y * unitSize) + new Vector3(0f, 0f, unitSize / 2f),
                            Quaternion.Euler(fallbackDoorRotationNS));
                }
                else if (dir == Vector2Int.right)
                {
                    DisableWall(currentRoom.roomObject, $"Wall_East_{localGrid.x}_{localGrid.y}", "Wall_East");

                    Vector2Int eastLocal = neighborGrid - neighborRoom.originGrid;
                    DisableWall(neighborRoom.roomObject, $"Wall_West_{eastLocal.x}_{eastLocal.y}", "Wall_West");

                    if (IsServer)
                        SpawnDoor(currentRoom.roomObject, $"Socket_East_{localGrid.x}_{localGrid.y}", "Socket_East",
                            new Vector3(currentGrid.x * unitSize, 0f, currentGrid.y * unitSize) + new Vector3(unitSize / 2f, 0f, 0f),
                            Quaternion.Euler(fallbackDoorRotationEW));
                }
            }
        }
    }

    private void DisableWall(GameObject room, string detailedName, string fallbackName)
    {
        Transform wall = FindChildRecursive(room.transform, detailedName)
            ?? FindChildRecursive(room.transform, fallbackName);
        wall?.gameObject.SetActive(false);
    }

    private void SpawnDoor(GameObject room, string socketDetailed, string socketFallback, Vector3 fallbackPos, Quaternion fallbackRot)
    {
        Transform socket = FindChildRecursive(room.transform, socketDetailed)
            ?? FindChildRecursive(room.transform, socketFallback);

        if (socket != null) InstantiateRoomObject(doorPrefab, socket.position, socket.rotation);
        else InstantiateRoomObject(doorPrefab, fallbackPos, fallbackRot);
    }

    private void SpawnFurnitureInRooms()
    {
        var checkedRooms = new List<GameObject>();

        foreach (var cell in roomGridMap)
        {
            if (cell.Value == null || checkedRooms.Contains(cell.Value.roomObject)) continue;

            GameObject roomObj = cell.Value.roomObject;
            checkedRooms.Add(roomObj);

            var spawnedUniques = new HashSet<GameObject>();

            foreach (Transform child in roomObj.GetComponentsInChildren<Transform>(false))
            {
                if (!child.name.StartsWith("Socket_Furniture")) continue;

                List<FurnitureData> pool = null;
                if (child.name.Contains("_Wall")) pool = cell.Value.assignedWallPool;
                else if (child.name.Contains("_Center")) pool = cell.Value.assignedCenterPool;

                if (pool == null || pool.Count == 0) continue;

                var available = new List<FurnitureData>();
                foreach (var f in pool)
                {
                    if (f.isUnique && spawnedUniques.Contains(f.prefab)) continue;
                    available.Add(f);
                }

                if (available.Count == 0) continue;

                ShuffleList(available);

                foreach (var furniture in available)
                {
                    if (furniture.prefab == null || Random.value > furniture.spawnChance) continue;

                    BoxCollider boxCol = furniture.prefab.GetComponentInChildren<BoxCollider>();
                    if (boxCol != null)
                    {
                        Vector3 worldCenter = child.position + child.rotation * (boxCol.transform.localPosition + boxCol.center);
                        Vector3 halfExtents = boxCol.size * 0.5f * 0.95f;
                        Quaternion worldRot = child.rotation * boxCol.transform.localRotation;

                        if (Physics.OverlapBox(worldCenter, halfExtents, worldRot, furnitureLayer).Length > 0) continue;
                    }

                    GameObject furnitureObj = Instantiate(furniture.prefab, child.position, child.rotation);
                    furnitureObj.transform.SetParent(roomObj.transform);
                    Physics.SyncTransforms();

                    if (furniture.isUnique) spawnedUniques.Add(furniture.prefab);
                    break;
                }
            }
        }
    }

    private void SpawnFoodItemsWithRaycast()
    {
        if (!IsServer || foodItemPool.Count == 0) return;

        var checkedRooms = new List<GameObject>();

        foreach (var cell in roomGridMap)
        {
            if (cell.Value == null || checkedRooms.Contains(cell.Value.roomObject)) continue;

            RoomInstance room = cell.Value;
            checkedRooms.Add(room.roomObject);

            int totalCells = room.size.x * room.size.y;
            int attempts = Random.Range(minItemsPerUnit, maxItemsPerUnit + 1) * totalCells;

            Vector3 center = room.roomObject.transform.position;
            float halfW = room.size.x * unitSize * 0.5f;
            float halfL = room.size.y * unitSize * 0.5f;

            // 벽 근처 스폰 방지를 위해 5 유닛 안쪽 범위로 제한
            float minX = center.x - halfW + 5f, maxX = center.x + halfW - 5f;
            float minZ = center.z - halfL + 5f, maxZ = center.z + halfL - 5f;

            for (int i = 0; i < attempts; i++)
            {
                ItemData item = foodItemPool[Random.Range(0, foodItemPool.Count)];
                if (item.prefab == null || Random.value > item.spawnChance) continue;

                Vector3 rayOrigin = new Vector3(Random.Range(minX, maxX), 20f, Random.Range(minZ, maxZ));
                if (!TryGetHighestPoint(rayOrigin, out Vector3 spawnPos)) continue;

                GameObject obj = Instantiate(item.prefab, spawnPos, Quaternion.identity);
                obj.GetComponent<NetworkObject>()?.Spawn();
            }
        }
    }

    private void SpawnEnemiesWithRaycast()
    {
        if (!IsServer) return;

        var checkedRooms = new List<GameObject>();
        var nonLivingRooms = new List<RoomInstance>();
        int spawnedSpiderCount = 0;

        foreach (var cell in roomGridMap)
        {
            if (cell.Value == null || checkedRooms.Contains(cell.Value.roomObject)) continue;

            RoomInstance room = cell.Value;
            checkedRooms.Add(room.roomObject);

            if (room.roomType == RoomType.LivingRoom)
            {
                if (catPrefab != null) SpawnEnemyInRoomBounds(room, catPrefab);
            }
            else
            {
                nonLivingRooms.Add(room);
            }
        }

        if (spiderData.prefab == null || nonLivingRooms.Count == 0) return;

        foreach (RoomInstance room in nonLivingRooms)
        {
            if (Random.value <= spiderData.spawnChance && SpawnEnemyInRoomBounds(room, spiderData.prefab))
                spawnedSpiderCount++;
        }

        // 확률로 한 마리도 안 나왔을 경우 최소 1마리 보장
        if (spawnedSpiderCount == 0)
        {
            RoomInstance fallback = nonLivingRooms[Random.Range(0, nonLivingRooms.Count)];
            SpawnEnemyInRoomBounds(fallback, spiderData.prefab);
        }
    }

    private bool SpawnEnemyInRoomBounds(RoomInstance room, GameObject enemyPrefab)
    {
        Vector3 center = room.roomObject.transform.position;
        float halfW = room.size.x * unitSize * 0.5f;
        float halfL = room.size.y * unitSize * 0.5f;

        // 벽 근처 스폰 방지를 위해 6 유닛 안쪽 범위로 제한
        float minX = center.x - halfW + 6f, maxX = center.x + halfW - 6f;
        float minZ = center.z - halfL + 6f, maxZ = center.z + halfL - 6f;

        Vector3 rayOrigin = new Vector3(Random.Range(minX, maxX), 20f, Random.Range(minZ, maxZ));
        if (!TryGetHighestPoint(rayOrigin, out Vector3 spawnPos)) return false;

        GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
        enemy.GetComponent<NetworkObject>()?.Spawn();
        return true;
    }

    // 레이캐스트로 가장 높은 표면 위치를 구함
    private bool TryGetHighestPoint(Vector3 rayOrigin, out Vector3 result)
    {
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, 40f, itemSpawnRaycastMask);
        if (hits.Length == 0) { result = Vector3.zero; return false; }

        result = hits[0].point;
        for (int i = 1; i < hits.Length; i++)
        {
            if (hits[i].point.y > result.y) result = hits[i].point;
        }
        return true;
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

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int r = Random.Range(0, i + 1);
            (list[i], list[r]) = (list[r], list[i]);
        }
    }

    private void ShuffleArray(Vector2Int[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int r = Random.Range(0, i + 1);
            (array[i], array[r]) = (array[r], array[i]);
        }
    }
}
