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
    [SerializeField] private Button hostBtn;  // 방장 버튼
    [SerializeField] private Button clientBtn; // 참가자 버튼
    [SerializeField] private Button quitBtn;  // 종료 버튼
    [SerializeField] private Button mainMenuSettingBtn; // 메인 메뉴 설정

    [Header("클라이언트 접속 전용 UI")]
    [SerializeField] private TMP_InputField codeInputField; // 방 코드 입력 필드
    [SerializeField] private Button joinConfirmBtn;    // 방 코드로 참가 버튼
    [SerializeField] private Button backBtn;             // 뒤로 가기 버튼

    [Header("방장 전용 UI")]
    [SerializeField] private TextMeshProUGUI codeText;   // 방 코드 표시 텍스트

    [Header("인게임 일시정지 UI")]
    [SerializeField] private GameObject pausePanel;   // 일시정지 패널
    [SerializeField] private Button resumeBtn;       // 일시정지 해제 버튼
    [SerializeField] private Button disconnectBtn;   // 서버 연결 끊고 메인 메뉴로 돌아가기 버튼
    [SerializeField] private Button inGameQuitBtn;    // 게임 종료 버튼
    [SerializeField] private Button inGameSettingBtn; // 인게임 설정

    [Header("설정 UI")]
    [SerializeField] private GameObject settingPanel; // 설정 창 UI
    [SerializeField] private Button closeSettingBtn;  // 설정 창 안의 닫기 버튼
    [SerializeField] private Toggle windowModeToggle; // 창모드 토글

    private async void Start()  // 게임 시작 시 초기화 및 UI 설정
    {
        ShowMainMenu();
        codeText.gameObject.SetActive(false);  // 방 코드 텍스트는 처음에 숨김
        pausePanel.SetActive(false);         // 일시정지 패널도 처음에는 숨김
        settingPanel.SetActive(false);      // 게임 시작 시 설정창 숨기기

        if (UnityServices.State != ServicesInitializationState.Initialized)  // 유니티 서비스가 초기화되지 않았다면 초기화 진행
        {
            await UnityServices.InitializeAsync();                          // 유니티 서비스 초기화
        }

        if (!AuthenticationService.Instance.IsSignedIn)                  // 유니티 클라우드에 익명으로 로그인되어 있지 않다면 로그인 시도
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log("유니티 클라우드 익명 로그인 완료!");
        }

        // 게임 시작 시 현재 화면 상태를 체크해서 토글에 반영 (전체화면이 아니면 창모드 켜짐)
        if (windowModeToggle != null)
        {
            windowModeToggle.isOn = !Screen.fullScreen;
            windowModeToggle.onValueChanged.AddListener(SetWindowMode);
        }

        // 함수 연결
        hostBtn.onClick.AddListener(StartRelayHost);
        clientBtn.onClick.AddListener(ShowInputUI);
        joinConfirmBtn.onClick.AddListener(StartRelayClient);
        backBtn.onClick.AddListener(ShowMainMenu);
        quitBtn.onClick.AddListener(QuitGame);
        mainMenuSettingBtn.onClick.AddListener(OpenSettings); //메인 설정

        resumeBtn.onClick.AddListener(TogglePauseMenu);
        disconnectBtn.onClick.AddListener(DisconnectAndReturnToMenu);
        inGameQuitBtn.onClick.AddListener(QuitGame);
        inGameSettingBtn.onClick.AddListener(OpenSettings); //인게임 설정
        closeSettingBtn.onClick.AddListener(CloseSettings); // 설정창 닫기 버튼
    }

    private void Update()  // 매 프레임마다 일시정지 메뉴 토글을 위한 입력 감지
    {
        if (NetworkManager.Singleton == null) return;  // 네트워크 매니저가 존재하지 않으면 입력 감지 안 함

        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)  // 게임이 진행 중일 때만 일시정지 메뉴 토글 입력 감지
        {
            if (Input.GetKeyDown(KeyCode.Escape))  // Escape 키를 눌렀을 때 일시정지 메뉴 토글
            {
                TogglePauseMenu();
            }
        }
    }

    private void TogglePauseMenu()  // 일시정지 메뉴 활성화/비활성화 및 커서 상태 조절
    {
        bool isActive = !pausePanel.activeSelf;  // 현재 일시정지 패널의 활성화 상태를 반전시킴
        pausePanel.SetActive(isActive);          // 일시정지 패널의 활성화 상태를 설정

        if (isActive) // 일시정지 메뉴가 활성화되면 커서를 보이게 하고 잠금 해제, 그렇지 않으면 커서를 숨기고 잠금
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

    private void DisconnectAndReturnToMenu()  // 서버와의 연결을 끊고 메인 메뉴로 돌아가는 기능
    {
        Debug.Log("서버와의 연결을 끊습니다.");
        NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ShowMainMenu()  // 메인 메뉴 UI 활성화 및 커서 상태 조절
    {
        Cursor.lockState = CursorLockMode.None;  // 메인 메뉴에서는 커서를 보이게 하고 잠금 해제
        Cursor.visible = true;

        hostBtn.gameObject.SetActive(true);
        clientBtn.gameObject.SetActive(true);
        quitBtn.gameObject.SetActive(true);
        mainMenuSettingBtn.gameObject.SetActive(true); // 추가

        codeInputField.gameObject.SetActive(false);
        joinConfirmBtn.gameObject.SetActive(false);
        backBtn.gameObject.SetActive(false);
    }

    private void ShowInputUI() // 클라이언트 참가를 위한 UI 활성화 및 메인 메뉴 버튼 비활성화
    {
        hostBtn.gameObject.SetActive(false);
        clientBtn.gameObject.SetActive(false);
        quitBtn.gameObject.SetActive(false);

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

    private void QuitGame()  // 게임 종료 기능, 에디터에서는 플레이 모드를 종료하고, 빌드된 게임에서는 애플리케이션을 종료
    {
        Debug.Log("게임을 종료합니다.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;  // 에디터에서는 플레이 모드를 종료
#else
        Application.Quit();  // 빌드된 게임에서는 애플리케이션을 종료
#endif
    }

    // 설정창 열기
    private void OpenSettings()
    {
        Debug.Log("설정창 열기 버튼 눌림!");
        settingPanel.SetActive(true);
    }

    // 설정창 닫기
    private void CloseSettings()
    {
        Debug.Log("설정창 닫힘!");
        settingPanel.SetActive(false);
    }

    // 창모드
    private void SetWindowMode(bool isWindowed)
    {
        if (isWindowed)
        {
            // 체크박스가 켜지면 창모드로 변경
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.SetResolution(1280, 720, false); //화면 크기 지정
            Debug.Log("창모드로 전환됨");
        }
        else
        {
            // 체크박스가 꺼지면 전체화면(테두리 없는 전체화면)으로 변경
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Debug.Log("전체화면으로 전환됨");
        }
    }

    private void HideAllUI()  // 모든 UI 요소를 비활성화하여 게임 화면만 보이도록 설정
    {
        hostBtn.gameObject.SetActive(false);
        clientBtn.gameObject.SetActive(false);
        quitBtn.gameObject.SetActive(false);
        codeInputField.gameObject.SetActive(false);
        joinConfirmBtn.gameObject.SetActive(false);
        backBtn.gameObject.SetActive(false);
        mainMenuSettingBtn.gameObject.SetActive(false); //추가

        settingPanel.SetActive(false); // 추가
    }
}