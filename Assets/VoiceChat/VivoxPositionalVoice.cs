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

    // ? [새로 추가됨] 중복 실행을 완벽하게 막아주는 철벽 플래그
    private bool isJoining = false;

    public override async void OnNetworkSpawn()
    {
        if (IsServer)
        {
            string randomRoomId = "Room_" + System.Guid.NewGuid().ToString().Substring(0, 8);
            dynamicChannelName.Value = randomRoomId;
        }

        if (IsOwner)
        {
            // ?? 핵심 방어: 이미 채널 진입 작업을 시작했거나, 방에 들어간 상태라면 즉시 컷! (중복 로그인 버그 원천 차단)
            if (isJoining || isChannelJoined) return;
            isJoining = true;

            try
            {
                // 유니티 인증이 끝날 때까지 얌전히 대기
                while (!Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
                {
                    await Task.Delay(100);
                }

                if (isEchoTestMode)
                {
                    myJoinedChannel = "EchoTestRoom";
                    await VivoxService.Instance.JoinEchoChannelAsync(myJoinedChannel, ChatCapability.AudioOnly);

                    isChannelJoined = true;
                    Debug.Log("[Vivox] ?? 에코 테스트 모드 입장 완료");
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
                    Debug.Log($"[Vivox] ?? {myJoinedChannel} 방에 3D 음성으로 입장 완료");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Vivox] 채널 입장 실패: {e}");
            }
            finally
            {
                // 입장이 성공하든 에러가 나든 진입 중 상태는 풀어줍니다.
                isJoining = false;
            }
        }
    }

    void Update()
    {
        if (IsOwner && isChannelJoined && !isEchoTestMode)
        {
            VivoxService.Instance.Set3DPosition(gameObject, myJoinedChannel);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && !string.IsNullOrEmpty(myJoinedChannel) && isChannelJoined)
        {
            VivoxService.Instance.LeaveChannelAsync(myJoinedChannel);
            isChannelJoined = false;
        }
    }
}