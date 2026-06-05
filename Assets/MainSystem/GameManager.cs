using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    public NetworkVariable<int> sharedEggCount = new NetworkVariable<int>(
    100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public static GameManager Instance; // ��𼭵� ���� ������ �� �ֵ��� �̱��� ���� ����

    [HideInInspector]
    public Vector3 startRoomPosition; // �� �����Ⱑ �˷��� ���� ���� ��ǥ

    private void Awake()
    {
        // ���� GameManager�� �� �ϳ��� �����ϵ��� ����
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // �ƹ��� ��ư�� ������ ������ �� �Լ��� �����Ͽ� ��� �÷��̾ �̵���Ŵ
    [ServerRpc(RequireOwnership = false)]
    public void StartGameServerRpc()
    {
        Debug.Log("[�ý���] ���� ����! ��� �ĸ��� �������� �����մϴ�.");

        // ���� �ִ� ��� �ĸ�(PlayerMovement)�� ã��
        PlayerMovement[] allPlayers = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);

        foreach (PlayerMovement player in allPlayers)
        {
            // �� �ĸ����� Ŭ���̾�Ʈ ȭ�鿡 �ڷ���Ʈ ������ ����
            player.TeleportClientRpc(startRoomPosition);
        }
    }
}