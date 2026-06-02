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
}