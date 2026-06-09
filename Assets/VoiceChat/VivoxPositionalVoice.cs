using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using Unity.Services.Vivox;
using System.Threading.Tasks;

public class VivoxPositionalVoice : NetworkBehaviour
{
    [Header("보이스 테스트 설정")]
    [Tooltip("체크하면 마이크 확인용 메아리 방으로 들어감.")]
    [SerializeField] private bool isEchoTestMode = false; // 실제 유저 테스트를 위해 기본값을 false로 두는 것을 추천합니다.

    public NetworkVariable<FixedString32Bytes> dynamicChannelName = new NetworkVariable<FixedString32Bytes>(
        "", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private string myJoinedChannel = "";
    private bool isChannelJoined = false;

    // 중복 실행을 완벽하게 막아주는 철벽 플래그
    private bool isJoining = false;

    // ? [추가됨] 위치 업데이트 쿨타임 변수 (5100 에러 방지)
    private float positionUpdateTimer = 0f;
    private const float POSITION_UPDATE_INTERVAL = 0.3f; // 0.3초마다 서버로 위치 전송

    public override async void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // ? [수정됨] 모든 플레이어가 동일한 방에서 만나도록 방 이름 고정
            dynamicChannelName.Value = "FlyGameVoiceRoom_01";
        }

        if (IsOwner)
        {
            // 핵심 방어: 이미 채널 진입 작업을 시작했거나, 방에 들어간 상태라면 즉시 컷! 
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
                    Debug.Log("[Vivox] ??? 에코 테스트 모드 입장 완료");
                }
                else
                {
                    while (dynamicChannelName.Value.ToString() == "")
                    {
                        await Task.Delay(100);
                    }

                    myJoinedChannel = dynamicChannelName.Value.ToString();

                    // ? [수정됨] 3D 보이스 거리 대폭 확장 (최대 600, 선명함 50) 및 선형 감소 적용
                    Channel3DProperties properties = new Channel3DProperties(600, 50, 1.0f, AudioFadeModel.InverseByDistance);

                    await VivoxService.Instance.JoinPositionalChannelAsync(myJoinedChannel, ChatCapability.AudioOnly, properties);

                    isChannelJoined = true;
                    Debug.Log($"[Vivox] ??? {myJoinedChannel} 방에 3D 음성으로 입장 완료");

                    // ? [수정됨] 접속 직후 마이크 음소거 강제 해제
                    if (VivoxService.Instance.IsInputDeviceMuted)
                    {
                        VivoxService.Instance.UnmuteInputDevice();
                        Debug.Log("[Vivox] 마이크 음소거 강제 해제 완료! 송신을 시작합니다.");
                    }
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
        if (IsOwner && isChannelJoined && !string.IsNullOrEmpty(myJoinedChannel) && !isEchoTestMode)
        {
            // 1. 방 명단에 우리가 들어간 방 이름이 있는지 확인합니다.
            if (VivoxService.Instance.ActiveChannels.ContainsKey(myJoinedChannel))
            {
                positionUpdateTimer += Time.deltaTime;

                if (positionUpdateTimer >= POSITION_UPDATE_INTERVAL)
                {
                    positionUpdateTimer = 0f;

                    try
                    {
                        // 2. 위치 전송을 시도합니다.
                        VivoxService.Instance.Set3DPosition(gameObject, myJoinedChannel);
                    }
                    catch (System.InvalidOperationException)
                    {
                        // 오디오 선이 100% 연결되기 전(약 0.5초)에 발생하는 "연결 안 됨" 에러는 조용히 무시합니다.
                        // (이렇게 하면 빨간 에러 창도 안 뜨고, 파리가 튕기지도 않습니다!)
                    }
                    catch (System.Exception e)
                    {
                        // 게임 끄는 순간 등 기타 상황의 에러는 노란색 경고로만 띄웁니다.
                        Debug.LogWarning($"[Vivox] 3D 위치 전송 무시됨: {e.Message}");
                    }
                }
            }
            else
            {
                // 방 목록에서 진짜로 사라졌을 때만 통신을 끕니다.
                Debug.LogWarning("[Vivox] ?? 채널 연결이 끊어졌습니다. 통신을 중단합니다.");
                isChannelJoined = false;
            }
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