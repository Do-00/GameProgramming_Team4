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
    }

    private class RoomInstance
    {
        public GameObject roomObject;
        public Vector2Int originGrid;
        public RoomType roomType;
        public Vector2Int size;
    }

    [Header("프리팹 데이터 베이스")]
    [SerializeField] private GameObject livingRoomPrefab;
    [SerializeField] private GameObject bathroomPrefab;
    [SerializeField] private GameObject doorPrefab;
    [SerializeField] private List<RoomPrefabData> normalRoomVariants = new List<RoomPrefabData>();

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

    // ? OnNetworkSpawn 시점에 서버가 시드를 생성하여 뿌립니다.
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            int randomSeed = Random.Range(1, 100000);

            // 서버 자신도 맵을 생성하고
            StartCoroutine(GenerateMapNextFrame(randomSeed));
            // 클라이언트들에게도 동일한 시드로 생성하라고 명령합니다.
            GenerateMapClientRpc(randomSeed);
        }
    }

    // ? 클라이언트들이 서버와 동일한 시드를 원격으로 전달받는 통로
    [ClientRpc]
    private void GenerateMapClientRpc(int seed)
    {
        if (IsServer) return; // 서버는 이미 위에서 생성 중이므로 중복 실행 방지
        StartCoroutine(GenerateMapNextFrame(seed));
    }

    private IEnumerator GenerateMapNextFrame(int seed)
    {
        yield return null;
        GenerateValidHouse(seed);
    }

    private void GenerateValidHouse(int seed)
    {
        // ?? [가장 중요] 동일한 난수 카드를 뽑도록 시드를 고정합니다.
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

                Debug.Log($"[MapGenerator] 시드({seed}) {attempts}번째 시도 성공! 최종 방 개수: {totalRoomCount}개");

                // ?? 문(Door) 오브젝트 동기화: 문 생성은 오직 서버에서만 Spawn을 선언하여 멀티 동기화를 제어합니다.
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

        RoomInstance lrInstance = new RoomInstance { roomObject = lrObj, originGrid = new Vector2Int(0, 0), roomType = RoomType.LivingRoom, size = new Vector2Int(2, 2) };
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
                    break;
                }
            }

            if (selectedPrefab == null && normalRoomVariants.Count > 0)
            {
                selectedPrefab = normalRoomVariants[0].prefab;
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
        RoomInstance roomInstance = new RoomInstance { roomObject = roomObj, originGrid = gridPos, roomType = assignedType, size = roomSize };

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
                            // ?? 벽 비활성화는 서버와 클라이언트 '모두' 자기 컴퓨터에서 실행합니다.
                            string detailedWallNorth = $"Wall_North_{localGrid.x}_{localGrid.y}";
                            Transform wallNorth = FindChildRecursive(currentRoom.transform, detailedWallNorth);
                            if (wallNorth == null) wallNorth = FindChildRecursive(currentRoom.transform, "Wall_North");
                            if (wallNorth != null) wallNorth.gameObject.SetActive(false);

                            Vector2Int northLocalGrid = neighborGrid - neighborInstance.originGrid;
                            string detailedWallSouth = $"Wall_South_{northLocalGrid.x}_{northLocalGrid.y}";
                            Transform wallSouth = FindChildRecursive(neighborRoom.transform, detailedWallSouth);
                            if (wallSouth == null) wallSouth = FindChildRecursive(neighborRoom.transform, "Wall_South");
                            if (wallSouth != null) wallSouth.gameObject.SetActive(false);

                            // ?? 문 오브젝트 생성은 오직 '서버'만 수행합니다. (클라이언트는 스폰 동기화로 받아옴)
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
                            // ?? 벽 비활성화는 서버와 클라이언트 '모두' 실행
                            string detailedWallEast = $"Wall_East_{localGrid.x}_{localGrid.y}";
                            Transform wallEast = FindChildRecursive(currentRoom.transform, detailedWallEast);
                            if (wallEast == null) wallEast = FindChildRecursive(currentRoom.transform, "Wall_East");
                            if (wallEast != null) wallEast.gameObject.SetActive(false);

                            Vector2Int eastLocalGrid = neighborGrid - neighborInstance.originGrid;
                            string detailedWallWest = $"Wall_West_{eastLocalGrid.x}_{eastLocalGrid.y}";
                            Transform wallWest = FindChildRecursive(neighborRoom.transform, detailedWallWest);
                            if (wallWest == null) wallWest = FindChildRecursive(neighborRoom.transform, "Wall_West");
                            if (wallWest != null) wallWest.gameObject.SetActive(false);

                            // ?? 문 오브젝트 생성은 오직 '서버'만 수행
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