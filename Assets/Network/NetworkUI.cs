using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class NetworkUI : MonoBehaviour
{
    [Header("메인 메뉴 버튼")]
    [SerializeField] private Button hostBtn;
    [SerializeField] private Button clientBtn;
    [SerializeField] private Button quitBtn;
    [SerializeField] private Button mainMenuSettingBtn;

    [Header("클라이언트 접속 전용 UI")]
    [SerializeField] private TMP_InputField codeInputField;
    [SerializeField] private Button joinConfirmBtn;
    [SerializeField] private Button backBtn;

    [Header("방장 전용 UI")]
    [SerializeField] private TextMeshProUGUI codeText;

    [Header("인게임 일시정지 UI")]
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button resumeBtn;
    [SerializeField] private Button disconnectBtn;
    [SerializeField] private Button inGameQuitBtn;
    [SerializeField] private Button inGameSettingBtn;

    [Header("설정 UI")]
    [SerializeField] private GameObject settingPanel;
    [SerializeField] private Button closeSettingBtn;
    [SerializeField] private Toggle windowModeToggle;
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Toggle micToggle; // ✨ 마이크 On/Off 토글 추가

    private async void Start()
    {
        ShowMainMenu();
        codeText.gameObject.SetActive(false);
        pausePanel.SetActive(false);
        settingPanel.SetActive(false);

        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log("유니티 클라우드 익명 로그인 완료!");
        }

        // 창모드 토글 초기화
        if (windowModeToggle != null)
        {
            windowModeToggle.isOn = !Screen.fullScreen;
            windowModeToggle.onValueChanged.AddListener(SetWindowMode);
        }

        // 볼륨 슬라이더 초기화
        if (masterVolumeSlider != null)
        {
            float savedVolume = PlayerPrefs.GetFloat("MasterVolume", 1f);
            masterVolumeSlider.minValue = 0f;
            masterVolumeSlider.maxValue = 1f;
            masterVolumeSlider.value = savedVolume;
            AudioListener.volume = savedVolume;
            masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);
        }

        // ✨ 마이크 토글 초기화
        if (micToggle != null)
        {
            micToggle.onValueChanged.AddListener(OnMicToggleChanged);
        }

        // 버튼 함수 연결
        hostBtn.onClick.AddListener(StartRelayHost);
        clientBtn.onClick.AddListener(ShowInputUI);
        joinConfirmBtn.onClick.AddListener(StartRelayClient);
        backBtn.onClick.AddListener(ShowMainMenu);
        quitBtn.onClick.AddListener(QuitGame);
        mainMenuSettingBtn.onClick.AddListener(OpenSettings);

        resumeBtn.onClick.AddListener(TogglePauseMenu);
        disconnectBtn.onClick.AddListener(DisconnectAndReturnToMenu);
        inGameQuitBtn.onClick.AddListener(QuitGame);
        inGameSettingBtn.onClick.AddListener(OpenSettings);
        closeSettingBtn.onClick.AddListener(CloseSettings);
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null) return;

        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // 설정창이 열려있으면 설정창만 닫기
                if (settingPanel.activeSelf)
                {
                    CloseSettings();
                    return;
                }
                TogglePauseMenu();
            }
        }
    }

    private void TogglePauseMenu()
    {
        bool isActive = !pausePanel.activeSelf;
        pausePanel.SetActive(isActive);

        if (isActive)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void DisconnectAndReturnToMenu()
    {
        Debug.Log("서버와의 연결을 끊습니다.");
        NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ShowMainMenu()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        hostBtn.gameObject.SetActive(true);
        clientBtn.gameObject.SetActive(true);
        quitBtn.gameObject.SetActive(true);
        mainMenuSettingBtn.gameObject.SetActive(true);

        codeInputField.gameObject.SetActive(false);
        joinConfirmBtn.gameObject.SetActive(false);
        backBtn.gameObject.SetActive(false);
    }

    private void ShowInputUI()
    {
        hostBtn.gameObject.SetActive(false);
        clientBtn.gameObject.SetActive(false);
        quitBtn.gameObject.SetActive(false);
        mainMenuSettingBtn.gameObject.SetActive(false);

        codeInputField.gameObject.SetActive(true);
        joinConfirmBtn.gameObject.SetActive(true);
        backBtn.gameObject.SetActive(true);
    }

    private async void StartRelayHost()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            HideAllUI();
            codeText.gameObject.SetActive(true);
            codeText.text = "방 코드: " + joinCode;
            Debug.Log("입장 코드: " + joinCode);

            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);
            NetworkManager.Singleton.StartHost();
        }
        catch (RelayServiceException e)
        {
            Debug.LogError("방 생성 실패: " + e.Message);
        }
    }

    private async void StartRelayClient()
    {
        try
        {
            string joinCode = codeInputField.text;
            Debug.Log("다음 코드로 접속 시도 중... : " + joinCode);

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);
            NetworkManager.Singleton.StartClient();

            HideAllUI();
        }
        catch (RelayServiceException e)
        {
            Debug.LogError("참가 실패(코드를 확인하세요): " + e.Message);
        }
    }

    private void QuitGame()
    {
        Debug.Log("게임을 종료합니다.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OpenSettings()
    {
        Debug.Log("설정창 열기!");
        settingPanel.SetActive(true);
    }

    private void CloseSettings()
    {
        Debug.Log("설정창 닫힘!");
        settingPanel.SetActive(false);
    }

    private void SetWindowMode(bool isWindowed)
    {
        if (isWindowed)
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.SetResolution(1280, 720, false);
            Debug.Log("창모드로 전환됨");
        }
        else
        {
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Debug.Log("전체화면으로 전환됨");
        }
    }

    private void SetMasterVolume(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat("MasterVolume", value);
        Debug.Log($"마스터 볼륨: {value}");
    }

    // ✨ 마이크 토글 조절 함수 추가
    private void OnMicToggleChanged(bool isOn)
    {
        VivoxManager vivoxManager = FindFirstObjectByType<VivoxManager>();

        if (vivoxManager != null)
        {
            // isOn이 true면 마이크 켜짐 (isMuted = false)
            // isOn이 false면 마이크 꺼짐 (isMuted = true)
            vivoxManager.SetMicrophoneMute(!isOn);
            Debug.Log($"마이크 켜짐 상태: {isOn}");
        }
        else
        {
            Debug.LogWarning("VivoxManager를 찾을 수 없어 마이크 상태를 변경할 수 없습니다.");
        }
    }

    private void HideAllUI()
    {
        hostBtn.gameObject.SetActive(false);
        clientBtn.gameObject.SetActive(false);
        quitBtn.gameObject.SetActive(false);
        codeInputField.gameObject.SetActive(false);
        joinConfirmBtn.gameObject.SetActive(false);
        backBtn.gameObject.SetActive(false);
        mainMenuSettingBtn.gameObject.SetActive(false);

        settingPanel.SetActive(false);
    }
}