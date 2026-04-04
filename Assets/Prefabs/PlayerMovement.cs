using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("이동 설정")]
    [SerializeField] private float walkSpeed = 10f;      // 걷는 속도
    [SerializeField] private float sprintSpeed = 15f;     // 달리는 속도
    [SerializeField] private float flySpeed = 20f;       // 날 때의 대쉬 속도
    [SerializeField] private float flyStrafeSpeed = 12f; // 날 때의 측면 이동 속도

    [Header("점프 & 대쉬 설정")]
    [SerializeField] private float jumpForce = 8f;     // 점프 거리
    [SerializeField] private float dashSpeed = 40f;    // 대쉬 속도
    [SerializeField] private float groundDashDuration = 0.2f; // 지상 대쉬 지속 시간
    [SerializeField] private float flyDashDuration = 0.15f;  // 비행 대쉬 지속 시간
    [SerializeField] private float dashCooldown = 1.5f;  // 대쉬 쿨타임
    [SerializeField] private float dashFriction = 5f;  // 대쉬 감속 속도
    [SerializeField] private float groundCheckDistance = 1.1f; // 지면 체크 거리

    [Header("카메라 설정")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float mouseSensitivity = 2f;

    private NetworkVariable<float> netYRot = new NetworkVariable<float>(0f);

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
        }
        else
        {
            playerCamera.gameObject.SetActive(false);   // 다른 플레이어의 카메라는 비활성화 (자신의 카메라만 활성화)
            if (playerCamera.GetComponent<AudioListener>() != null)  // 다른 플레이어의 카메라에 붙어있는 AudioListener도 비활성화 (자신의 오디오만 들리도록)
                playerCamera.GetComponent<AudioListener>().enabled = false;  // 다른 플레이어의 카메라에 붙어있는 AudioListener도 비활성화 (자신의 오디오만 들리도록)

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

            HandleLook();   // 마우스 입력을 처리하여 카메라와 플레이어의 회전을 제어하는 메서드 호출
            HandleInput();  // 키보드와 마우스 입력을 처리하여 이동, 점프, 대쉬 등의 행동을 제어하는 메서드 호출
            SubmitRotationServerRpc(transform.eulerAngles.y);  // 자신의 Y축 회전을 서버로 전송하여 다른 플레이어들에게도 적용되도록 함

            animator.SetFloat("speed", horizontalSpeed); //걷는 속도 애니메이션 변수(speed)에 대입
            animator.SetFloat("speed", horizontalSpeed); //뛸 때 속도 애니메이션 변수(speed)에 대입
            animator.SetBool("flying", isFlying);
            animator.SetBool("dash", isDashing);


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
            isFlying = !isFlying;

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
        if (rb.useGravity == flyingState)
        {
            rb.useGravity = !flyingState;
            if (flyingState) rb.linearVelocity = Vector3.zero;
            else rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        }

        if (flyingState) rb.linearVelocity = velocity;
        else rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);
    }

    [ServerRpc]
    private void SubmitRotationServerRpc(float yRot) // 클라이언트에서 입력된 Y축 회전값을 서버로 전송하는 메서드
    {
        netYRot.Value = yRot;
    }
}