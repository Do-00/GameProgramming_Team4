using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("이동 설정")]
    [SerializeField] private float walkSpeed = 10f;      // 걷는 속도
    [SerializeField] private float sprintSpeed = 15f;     // 달리는 속도
    [SerializeField] private float flySpeed = 20f;       // 날 때의 대쉬 속도
    [SerializeField] private float flyStrafeSpeed = 12f; // 날 때의 측면 이동 속도

    [Header("점프 & 대쉬 설정")]
    [SerializeField] private float jumpForce = 15f;     // 점프 거리
    [SerializeField] private float fallMultiplier = 4f; // 낙하 가속도                -------- 새로 추가함
    [SerializeField] private float upwardMultiplier = 2.5f;  // 상승 가속도                -------- 새로 추가함
    [SerializeField] private float dashSpeed = 40f;    // 대쉬 속도
    [SerializeField] private float groundDashDuration = 0.2f; // 지상 대쉬 지속 시간
    [SerializeField] private float flyDashDuration = 0.15f;  // 비행 대쉬 지속 시간
    [SerializeField] private float dashCooldown = 1.5f;  // 대쉬 쿨타임
    [SerializeField] private float dashFriction = 5f;  // 대쉬 감속 속도
    [SerializeField] private float groundCheckDistance = 1.1f; // 지면 체크 거리

    [Header("비행 스태미너 설정")]
    [SerializeField] private float maxFlightStamina = 20f;       // 최대 비행 시간
    [SerializeField] private float staminaDrainRate = 1f;        // 초당 소모량
    [SerializeField] private float staminaRegenRate = 1.5f;      // 지상 대기 중 초당 회복량

    [Header("UI 설정")]
    [SerializeField] private GameObject playerUICanvas; // 내 화면에서만 켤 캔버스
    [SerializeField] private Slider staminaSlider;      // 스태미너 바 슬라이더

    // 서버가 엄격하게 관리하고 클라이언트들에게 실시간 복제할 네트워크 변수들
    public NetworkVariable<float> currentStamina = new NetworkVariable<float>(20f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> isFlightBlocked = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private float blockTimer = 0f;

    [Header("카메라 설정")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float mouseSensitivity = 2f;

    private NetworkVariable<float> netYRot = new NetworkVariable<float>(0f);

    [Header("에너지 & 상호작용 설정")]
    public NetworkVariable<int> currentEnergy = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    [SerializeField] private GameObject eggPrefab;
    [SerializeField] private float interactDistance = 4f; // 음식 섭취 최대 사거리
    [SerializeField] private float eatDuration = 3f; // 섭취에 걸리는 시간
    private Outline currentlyHighlightedFood; // 현재 하이라이트 중인 음식
    private float eatTimer = 0f;                     // 현재 얼마나 먹었는지 측정
    private bool isEating = false;                   // 현재 먹고 있는 중인지

    private bool isFlying = false;  // 날고 있는지 여부 (서버가 관리)
    private float xRotation = 0f;  // 카메라의 수직 회전값 (서버가 관리)

    private Rigidbody rb;
    private Vector3 inputVelocity = Vector3.zero;
    private Animator animator; //애니메이션 

    private bool isDashing = false;   // 대쉬 중인지 여부 (서버가 관리)
    private float dashTimer = 0f;     // 대쉬 지속 시간 타이머
    private float nextDashTime = 0f;  // 다음 대쉬 가능 시간
    private Vector3 dashDirection;    // 대쉬 방향 (서버가 관리)
    private float currentDashSpeed = 0f;  // 현재 대쉬 속도 (서버가 관리)


    private void Awake()
    {
        rb = GetComponent<Rigidbody>(); // Rigidbody 컴포넌트를 가져옴
        animator = GetComponent<Animator>(); //Animator 컴포넌트 가져옴
    }

    public override void OnNetworkSpawn() // 네트워크에 스폰될 때 호출되는 메서드
    {
        // 서버라면 최초 스태미너 값을 최대치로 엄격하게 설정
        if (IsServer)
        {
            currentStamina.Value = maxFlightStamina;
        }

        // 내가 조종하는 내 캐릭터일 때
        if (IsOwner)
        {
            playerCamera.gameObject.SetActive(true);  // 플레이어 카메라 활성화
            if (Camera.main != null) Camera.main.gameObject.SetActive(false);  // 메인 카메라가 있다면 비활성화

            Cursor.lockState = CursorLockMode.Locked; // 커서를 화면 중앙에 고정
            Cursor.visible = false;                 // 커서를 보이지 않게 설정

            Renderer[] renderers = GetComponentsInChildren<Renderer>();   // 자신의 모델을 가져와서 그림자만 보이도록 설정 (자신은 보이지 않게)
            foreach (Renderer r in renderers)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }

            // 내 화면의 UI와 슬라이더 기본값 초기화 세팅
            if (playerUICanvas != null) playerUICanvas.SetActive(true);
            if (staminaSlider != null)
            {
                staminaSlider.maxValue = maxFlightStamina;
                staminaSlider.value = maxFlightStamina;
            }
        }
        else
        {
            playerCamera.gameObject.SetActive(false);   // 다른 플레이어의 카메라는 비활성화 (자신의 카메라만 활성화)
            if (playerCamera.GetComponent<AudioListener>() != null)  // 다른 플레이어의 카메라에 붙어있는 AudioListener도 비활성화 (자신의 오디오만 들리도록)
                playerCamera.GetComponent<AudioListener>().enabled = false;  // 다른 플레이어의 카메라에 붙어있는 AudioListener도 비활성화 (자신의 오디오만 들리도록)

            // 다른 사람 화면에서는 내 UI 캔버스를 보이지 않게 가림
            if (playerUICanvas != null) playerUICanvas.SetActive(false);
        }
    }

    void Update() // 매 프레임마다 호출되는 메서드
    {
        if (IsOwner)
        {
            float horizontalSpeed = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z).magnitude; //실제 이동 속도 계산(수평)
            if (Cursor.lockState == CursorLockMode.None)  // 일시정지 메뉴가 열려있는 동안에는 입력을 받지 않도록 함
            {
                inputVelocity = Vector3.zero;  // 이동 입력 초기화
                return;
            }

            // 서버가 안전하게 갱신해준 동기화 데이터를 실시간으로 UI 슬라이더에 대입
            if (staminaSlider != null)
            {
                staminaSlider.value = currentStamina.Value;
            }

            // 만약 서버에서 스태미너가 고갈되었거나 기막에 차단당했다면 클라이언트 비행 변수도 꺼줌
            if (currentStamina.Value <= 0f || isFlightBlocked.Value)
            {
                isFlying = false;
            }

            HandleLook();   // 마우스 입력을 처리하여 카메라와 플레이어의 회전을 제어하는 메서드 호출
            HandleInput();  // 키보드와 마우스 입력을 처리하여 이동, 점프, 대쉬 등의 행동을 제어하는 메서드 호출
            SubmitRotationServerRpc(transform.eulerAngles.y);  // 자신의 Y축 회전을 서버로 전송하여 다른 플레이어들에게도 적용되도록 함
            HandleAimHighlight();  // 조준 중인 음식에 하이라이트를 적용하는 메서드 호출
            HandleEatingProgress(); // 음식 섭취 진행 상황을 처리하는 메서드 호출

            if (Input.GetKeyDown(KeyCode.E) && currentEnergy.Value >= 1)
            {
                LayEggsServerRpc();
            }

            animator.SetFloat("speed", horizontalSpeed); //걷는 속도 애니메이션 변수(speed)에 대입
            animator.SetFloat("speed", horizontalSpeed); //뛸 때 속도 애니메이션 변수(speed)에 대입
            animator.SetBool("flying", isFlying); //날 때 애니메이션 변수(flying)에 대입
            animator.SetBool("dash", isDashing);  //대쉬 애니메이션 변수(dash)에 대입
        }
        else
        {
            float smoothYRot = Mathf.LerpAngle(transform.eulerAngles.y, netYRot.Value, Time.deltaTime * 15f);  // 다른 플레이어의 Y축 회전을 네트워크에서 받아와서 부드럽게 보간하여 적용
            transform.rotation = Quaternion.Euler(0f, smoothYRot, 0f);
        }
    }

    void FixedUpdate() // 물리 업데이트마다 호출되는 메서드
    {
        if (!IsOwner) return;
        SubmitMovementServerRpc(inputVelocity, isFlying);  // 클라이언트에서 계산된 이동 벡터와 비행 상태를 서버로 전송하여 물리 이동을 처리하도록 함
    }

    // ✨ 외부 기믹(거미줄 등)이 서버에서 다이렉트로 호출할 비행 봉쇄 함수
    public void BlockFlight(float duration)
    {
        if (!IsServer) return;
        isFlightBlocked.Value = true;
        blockTimer = duration;
    }

    private void HandleLook() // 마우스 입력을 처리하여 카메라와 플레이어의 회전을 제어하는 메서드
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;  // 마우스 X축 입력을 감지하여 플레이어의 Y축 회전에 적용할 회전값 계산
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;  // 마우스 Y축 입력을 감지하여 카메라의 X축 회전에 적용할 회전값 계산

        xRotation -= mouseY;  // 마우스 Y축 입력을 카메라의 X축 회전에 적용 (위아래 회전)
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);  // 카메라의 X축 회전값을 -90도에서 90도로 제한하여 머리가 뒤로 넘어가지 않도록 함
        playerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);  // 카메라의 로컬 회전을 X축 회전값으로 설정하여 카메라가 플레이어의 머리 위치에서 위아래로 회전하도록 함

        transform.Rotate(Vector3.up * mouseX);  // 플레이어의 Y축 회전에 마우스 X축 입력을 적용하여 좌우 회전하도록 함
    }

    private void HandleInput() // 키보드와 마우스 입력을 처리하여 이동, 점프, 대쉬 등의 행동을 제어하는 메서드
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            // 스태미너가 확실하게 남아있고, 봉쇄 상태가 아닐 때만 비행 모드 진입 가능
            if (!isFlying && currentStamina.Value > 0f && !isFlightBlocked.Value)
            {
                isFlying = true;
            }
            else if (isFlying)
            {
                isFlying = false;
            }

            animator.SetBool("flying", isFlying);
        }

        if (isDashing)
        {
            dashTimer -= Time.deltaTime;

            animator.SetBool("dash", isDashing);

            if (dashTimer <= 0f) isDashing = false;
        }

        if (Input.GetMouseButtonDown(1) && Time.time >= nextDashTime)
        {
            isDashing = true;
            nextDashTime = Time.time + dashCooldown;
            currentDashSpeed = dashSpeed;

            if (isFlying)
            {
                dashDirection = playerCamera.transform.forward;
                dashTimer = flyDashDuration;
            }
            else
            {
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                Vector3 inputDir = (transform.right * h + transform.forward * v).normalized;
                dashDirection = inputDir.magnitude > 0.1f ? inputDir : transform.forward;
                dashTimer = groundDashDuration;
            }
        }

        if (Input.GetKeyDown(KeyCode.Space) && !isFlying && !isDashing && IsGrounded())
        {
            SubmitJumpServerRpc();
        }

        Vector3 baseVelocity = Vector3.zero;

        if (isFlying)
        {
            float vertical = Input.GetAxis("Vertical");
            float horizontal = Input.GetAxis("Horizontal");

            if (vertical > 0) baseVelocity += playerCamera.transform.forward * vertical * flySpeed;
            if (Mathf.Abs(horizontal) > 0.1f) baseVelocity += transform.right * horizontal * flyStrafeSpeed;
        }
        else
        {
            float horizontal = Input.GetAxis("Horizontal");
            float vertical = Input.GetAxis("Vertical");
            float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
            baseVelocity = (transform.right * horizontal + transform.forward * vertical) * currentSpeed;
        }

        if (isDashing)
        {
            inputVelocity = dashDirection * currentDashSpeed;
        }
        else
        {
            if (currentDashSpeed > 0.1f) currentDashSpeed = Mathf.Lerp(currentDashSpeed, 0f, Time.deltaTime * dashFriction);
            else currentDashSpeed = 0f;

            inputVelocity = baseVelocity + (dashDirection * currentDashSpeed);
        }
    }

    private bool IsGrounded() // 플레이어가 지면에 닿아있는지 확인하는 메서드
    {
        return Physics.Raycast(transform.position, Vector3.down, groundCheckDistance);
    }

    [ServerRpc]
    private void SubmitJumpServerRpc() // 클라이언트에서 점프 입력이 발생했을 때 서버로 점프 명령을 전송하는 메서드
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
    }

    [ServerRpc]
    private void SubmitMovementServerRpc(Vector3 velocity, bool flyingState) // 클라이언트에서 입력된 이동 벡터와 비행 상태를 서버로 전송하는 메서드
    {
        // 1. [서버 권한] 거미줄/끈끈이 디버프 타이머 처리 및 비행 상태 강제 차단
        if (isFlightBlocked.Value)
        {
            blockTimer -= Time.fixedDeltaTime;
            if (blockTimer <= 0f)
            {
                isFlightBlocked.Value = false;
            }
            flyingState = false;
        }

        // 2. [서버 권한] 비행 여부에 따른 실시간 스태미너 가감산 처리
        if (flyingState)
        {
            currentStamina.Value = Mathf.Max(0f, currentStamina.Value - (staminaDrainRate * Time.fixedDeltaTime));
            if (currentStamina.Value <= 0f)
            {
                flyingState = false; // 스태미너 소모 완료 시 비행 권한 해제하여 추락 유도
            }
        }
        else if (Physics.Raycast(transform.position, Vector3.down, groundCheckDistance)) // 서버 측에서 정확한 지면 판단
        {
            if (currentStamina.Value < maxFlightStamina)
            {
                currentStamina.Value = Mathf.Min(maxFlightStamina, currentStamina.Value + (staminaRegenRate * Time.fixedDeltaTime));
            }
        }

        // 3. 기존의 핵심 물리 계산 처리
        if (rb.useGravity == flyingState)
        {
            rb.useGravity = !flyingState;
            if (flyingState) rb.linearVelocity = Vector3.zero;
            else rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        }

        if (flyingState)
        {
            rb.linearVelocity = velocity;
        }
        else
        {
            rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);

            if (rb.linearVelocity.y < 0)
            {
                rb.linearVelocity += Vector3.up * Physics.gravity.y * (fallMultiplier - 1) * Time.fixedDeltaTime;
            }
            else if (rb.linearVelocity.y > 0)
            {
                rb.linearVelocity += Vector3.up * Physics.gravity.y * (upwardMultiplier - 1) * Time.fixedDeltaTime;
            }
        }
    }

    [ServerRpc]
    private void SubmitRotationServerRpc(float yRot) // 클라이언트에서 입력된 Y축 회전값을 서버로 전송하는 메서드
    {
        netYRot.Value = yRot;
    }
    private void HandleAimHighlight()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            if (hit.collider.CompareTag("Food"))
            {
                Outline foodOutline = hit.collider.GetComponent<Outline>();

                if (foodOutline != null && foodOutline != currentlyHighlightedFood)
                {
                    DisableCurrentHighlight(); // 이전 하이라이트 끄기
                    currentlyHighlightedFood = foodOutline;
                    currentlyHighlightedFood.enabled = true; // 새로운 하이라이트 켜기
                }
                return;
            }
        }

        DisableCurrentHighlight();
    }

    private void DisableCurrentHighlight()
    {
        if (currentlyHighlightedFood != null)
        {
            currentlyHighlightedFood.enabled = false;
            currentlyHighlightedFood = null;
        }
    }
    private void TryEatFood()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        // 광선을 쏴서 interactDistance(사거리) 안에 무언가 맞았는지 확인
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            // 맞은 물체의 태그가 Food라면
            if (hit.collider.CompareTag("Food"))
            {
                // 맞은 물체의 네트워크 오브젝트 정보를 가져와 서버로 보냄
                NetworkObject foodNetObj = hit.collider.GetComponent<NetworkObject>();
                if (foodNetObj != null)
                {
                    EatFoodServerRpc(foodNetObj);
                }
            }
        }
    }

    [ServerRpc]
    private void EatFoodServerRpc(NetworkObjectReference foodRef)
    {
        if (foodRef.TryGet(out NetworkObject foodNetObj))
        {
            if (IsOwner) DisableCurrentHighlight();

            currentEnergy.Value += 10;

            foodNetObj.Despawn(false); // 네트워크 해제

            Debug.Log("[서버] 음식을 섭취했습니다! 에너지: " + currentEnergy.Value);
        }
    }

    [ServerRpc]
    private void LayEggsServerRpc()
    {
        // 서버 측에서 다시 한번 에너지가 충분한지 확인
        if (currentEnergy.Value >= 1 && eggPrefab != null)
        {
            // 에너지 10 차감
            currentEnergy.Value -= 1;

            // 알 소환 위치 계산 (엉덩이 뒤쪽)
            Vector3 spawnPos = transform.position - transform.forward * 0.5f + Vector3.up * 0.2f + Random.insideUnitSphere * 0.1f;

            // 알 생성 및 네트워크 스폰
            GameObject newEgg = Instantiate(eggPrefab, spawnPos, Quaternion.identity);
            newEgg.GetComponent<NetworkObject>().Spawn();

            Debug.Log($"[서버] 알을 1개 낳았습니다. 남은 에너지: {currentEnergy.Value}");
        }
    }

    private void HandleEatingProgress()
    {
        // F키를 꾹 누르고 있고 + 조준 중인 음식이 있다면
        if (Input.GetKey(KeyCode.F) && currentlyHighlightedFood != null)
        {
            isEating = true;
            eatTimer += Time.deltaTime; // 시간 누적

            // 3초가 다 되었다면 섭취 완료
            if (eatTimer >= eatDuration)
            {
                NetworkObject foodNetObj = currentlyHighlightedFood.GetComponent<NetworkObject>();
                if (foodNetObj != null)
                {
                    EatFoodServerRpc(foodNetObj);
                }
                ResetEating(); // 먹었으니 타이머 초기화
            }

            // 테스트용 콘솔 출력
            Debug.Log($"음식 섭취 중... ({(eatTimer / eatDuration) * 100:F0}%)");
        }
        else
        {
            // 키를 떼거나 에임이 빗나가면 초기화
            if (isEating) ResetEating();
        }
    }
    private void ResetEating()
    {
        isEating = false;
        eatTimer = 0f;
    }
}