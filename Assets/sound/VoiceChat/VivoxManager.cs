using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

public class VivoxManager : MonoBehaviour
{
    async void Start()
    {
        try
        {
            while (UnityServices.State != ServicesInitializationState.Initialized || !AuthenticationService.Instance.IsSignedIn)
            {
                await Task.Delay(100);
            }
            Debug.Log($"[Vivox] 로그인 완료");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Vivox] 대기 중 오류 발생: {e}");
        }
    }

    public void SetMicrophoneMute(bool isMuted)
    {
        // Vivox가 켜져 있을 때만 작동
        if (VivoxService.Instance != null)
        {
            if (isMuted)
            {
                VivoxService.Instance.MuteInputDevice(); // 마이크 끄기 (음소거)
                Debug.Log("[Vivox] 마이크가 꺼졌습니다. (Mute)");
            }
            else
            {
                VivoxService.Instance.UnmuteInputDevice(); // 마이크 켜기
                Debug.Log("[Vivox] 마이크가 켜졌습니다. (Unmute)");
            }
        }
    }
}