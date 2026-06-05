using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    public NetworkVariable<int> sharedEggCount = new NetworkVariable<int>(
    0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public static GameManager Instance; // 어디서든 쉽게 접근할 수 있도록 싱글톤 패턴 적용

    [HideInInspector]
    public Vector3 startRoomPosition; // 맵 생성기가 알려줄 시작 방의 좌표

    private void Awake()
    {
        // 씬에 GameManager가 단 하나만 존재하도록 유지
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // 아무나 버튼을 누르면 서버가 이 함수를 실행하여 모든 플레이어를 이동시킴
    [ServerRpc(RequireOwnership = false)]
    public void StartGameServerRpc()
    {
        Debug.Log("[시스템] 게임 시작! 모든 파리를 지상으로 강하합니다.");

        // 씬에 있는 모든 파리(PlayerMovement)를 찾음
        PlayerMovement[] allPlayers = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);

        foreach (PlayerMovement player in allPlayers)
        {
            // 각 파리들의 클라이언트 화면에 텔레포트 명령을 내림
            player.TeleportClientRpc(startRoomPosition);
        }
    }
}