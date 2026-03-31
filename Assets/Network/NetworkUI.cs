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

    private async void Start()  // 게임 시작 시 초기화 및 UI 설정
    {
        ShowMainMenu();
        codeText.gameObject.SetActive(false);  // 방 코드 텍스트는 처음에 숨김
        pausePanel.SetActive(false);         // 일시정지 패널도 처음에는 숨김

        if (UnityServices.State != ServicesInitializationState.Initialized)  // 유니티 서비스가 초기화되지 않았다면 초기화 진행
        {
            await UnityServices.InitializeAsync();                          // 유니티 서비스 초기화
        }

        if (!AuthenticationService.Instance.IsSignedIn)                  // 유니티 클라우드에 익명으로 로그인되어 있지 않다면 로그인 시도
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log("유니티 클라우드 익명 로그인 완료!");
        }

        // 함수 연결
        hostBtn.onClick.AddListener(StartRelayHost);
        clientBtn.onClick.AddListener(ShowInputUI);
        joinConfirmBtn.onClick.AddListener(StartRelayClient);
        backBtn.onClick.AddListener(ShowMainMenu);
        quitBtn.onClick.AddListener(QuitGame);

        resumeBtn.onClick.AddListener(TogglePauseMenu);
        disconnectBtn.onClick.AddListener(DisconnectAndReturnToMenu);
        inGameQuitBtn.onClick.AddListener(QuitGame);
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

    private async void StartRelayHost() // 방장으로서 릴레이 서버에 방을 만들고, 참가자들이 접속할 수 있도록 코드 생성 및 네트워크 매니저 설정
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3); // 최대 3명의 참가자가 접속할 수 있는 방 생성
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);  // 생성된 방의 입장 코드를 가져옴

            HideAllUI();  // 방장이 방을 만들면 모든 UI를 숨기고 게임 화면만 보이도록 설정
            codeText.gameObject.SetActive(true);  // 방 코드 텍스트 활성화
            codeText.text = "방 코드: " + joinCode;
            Debug.Log("입장 코드: " + joinCode);

            RelayServerData relayServerData = new RelayServerData(allocation, "dtls");  // 릴레이 서버 데이터를 생성하여 네트워크 매니저의 트랜스포트에 설정
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);  // 네트워크 매니저의 트랜스포트에 릴레이 서버 데이터 설정

            NetworkManager.Singleton.StartHost();
        }
        catch (RelayServiceException e)
        {
            Debug.LogError("방 생성 실패: " + e.Message);
        }
    }

    private async void StartRelayClient()  // 참가자로서 입력된 방 코드로 릴레이 서버에 접속을 시도하고, 네트워크 매니저를 클라이언트 모드로 시작
    {
        try
        {
            string joinCode = codeInputField.text;  // 입력된 방 코드 가져오기
            Debug.Log("다음 코드로 접속 시도 중 : " + joinCode);

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);  // 입력된 방 코드로 릴레이 서버에 접속 시도하여 참가자 정보를 가져옴

            RelayServerData relayServerData = new RelayServerData(joinAllocation, "dtls");  // 릴레이 서버 데이터를 생성하여 네트워크 매니저의 트랜스포트에 설정
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);  // 네트워크 매니저의 트랜스포트에 릴레이 서버 데이터 설정

            NetworkManager.Singleton.StartClient();

            HideAllUI(); // 참가자가 방에 접속하면 모든 UI를 숨기고 게임 화면만 보이도록 설정
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

    private void HideAllUI()  // 모든 UI 요소를 비활성화하여 게임 화면만 보이도록 설정
    {
        hostBtn.gameObject.SetActive(false);
        clientBtn.gameObject.SetActive(false);
        quitBtn.gameObject.SetActive(false);
        codeInputField.gameObject.SetActive(false);
        joinConfirmBtn.gameObject.SetActive(false);
        backBtn.gameObject.SetActive(false);
    }
}