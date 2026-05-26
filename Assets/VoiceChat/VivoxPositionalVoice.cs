using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using Unity.Services.Vivox;
using System.Threading.Tasks;

public class VivoxPositionalVoice : NetworkBehaviour
{
    [Header("보이스 테스트 설정")]
    [Tooltip("체크하면 마이크 확인용 메아리 방으로 들어감.")]
    [SerializeField] private bool isEchoTestMode = true; 

    public NetworkVariable<FixedString32Bytes> dynamicChannelName = new NetworkVariable<FixedString32Bytes>(
        "", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private string myJoinedChannel = "";
    private bool isChannelJoined = false;

    public override async void OnNetworkSpawn()
    {
        if (IsServer)
        {
            string randomRoomId = "Room_" + System.Guid.NewGuid().ToString().Substring(0, 8);
            dynamicChannelName.Value = randomRoomId;
        }

        if (IsOwner)
        {
            try
            {
                if (isEchoTestMode)
                {
                    myJoinedChannel = "EchoTestRoom";
                    await VivoxService.Instance.JoinEchoChannelAsync(myJoinedChannel, ChatCapability.AudioOnly);

                    isChannelJoined = true;
                }
                else
                {
                    while (dynamicChannelName.Value.ToString() == "")
                    {
                        await Task.Delay(100);
                    }

                    myJoinedChannel = dynamicChannelName.Value.ToString();
                    Channel3DProperties properties = new Channel3DProperties(40, 5, 1.0f, AudioFadeModel.InverseByDistance);

                    await VivoxService.Instance.JoinPositionalChannelAsync(myJoinedChannel, ChatCapability.AudioOnly, properties);

                    isChannelJoined = true;
                    Debug.Log($"[Vivox] ?? {myJoinedChannel} 방에 3D 음성으로 입장");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Vivox] 채널 입장 실패: {e}");
            }
        }
    }

    void Update()
    {
        // 에코 모드가 아닐 때만 3D 위치를 전송 멀티 확인 후에는 에코 모드와 같이 지워주기
        if (IsOwner && isChannelJoined && !isEchoTestMode)
        {
            VivoxService.Instance.Set3DPosition(gameObject, myJoinedChannel);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && !string.IsNullOrEmpty(myJoinedChannel))
        {
            VivoxService.Instance.LeaveChannelAsync(myJoinedChannel);
        }
    }
}