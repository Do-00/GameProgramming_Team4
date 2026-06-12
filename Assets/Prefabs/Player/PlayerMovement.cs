using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : NetworkBehaviour
{
    private enum PlayerSoundType { Damage, Die, Eat, Plop }

    [Header("이동 설정")]
    [SerializeField] private float walkSpeed = 10f;
    [SerializeField] private float sprintSpeed = 15f;
    [SerializeField] private float flySpeed = 20f;
    [SerializeField] private float flyStrafeSpeed = 12f;

    [Header("점프 & 대시 설정")]
    [SerializeField] private float jumpForce = 15f;
    [SerializeField] private float fallMultiplier = 4f;      // 하강 시 중력 가중치
    [SerializeField] private float upwardMultiplier = 2.5f;  // 상승 시 중력 가중치
    [SerializeField] private float dashSpeed = 40f;
    [SerializeField] private float groundDashDuration = 0.2f;
    [SerializeField] private float flyDashDuration = 0.15f;
    [SerializeField] private float dashCooldown = 1.5f;
    [SerializeField] private float dashFriction = 5f;        // 대시 종료 후 속도 감쇠 계수
    [SerializeField] private float groundCheckDistance = 1.1f;

    [Header("비행 스태미너 설정")]
    [SerializeField] private float maxFlightStamina = 20f;
    [SerializeField] private float staminaDrainRate = 1f;
    [SerializeField] private float staminaRegenRate = 1.5f;

    [Header("사망 & 관전 설정")]
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> deathCount = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> timeOfDeath = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private int currentSpectateIndex = 0;
    private Transform spectateTarget;
    private Vector3 initialCameraLocalPos;
    private Quaternion initialCameraLocalRot;

    [Header("체력 설정")]
    [SerializeField] private float maxHealth = 100f;

    [Header("UI 연결")]
    [SerializeField] private GameObject playerUICanvas;
    [SerializeField] private GameObject gameScreenView; // 사망 중에도 유지할 게임 화면 패널
    [SerializeField] private GameObject quotaText;      // 사망 중에도 유지할 할당량 텍스트
    [SerializeField] private Slider staminaSlider;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Slider satietySlider;

    public NetworkVariable<float> currentStamina = new NetworkVariable<float>(20f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> isFlightBlocked = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private float blockTimer = 0f;

    [Header("카메라 설정")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float mouseSensitivity = 2f;

    private NetworkVariable<float> netYRot = new NetworkVariable<float>(0f);

    [Header("에너지 & 상호작용 설정")]
    public NetworkVariable<int> currentEnergy = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    [SerializeField] private int maxEnergy = 100;
    [SerializeField] private GameObject eggPrefab;
    [SerializeField] private float interactDistance = 4f;
    [SerializeField] private float eatDuration = 3f;  // F키를 이 시간 동안 누르고 있어야 음식 섭취 완료

    private Outline currentlyHighlightedFood;
    private float eatTimer = 0f;
    private bool isEating = false;

    [Header("알 운반 설정")]
    [SerializeField] private Transform holdPoint;  // 알을 들고 있을 때 알이 붙는 위치
    private NetworkObject carriedEgg = null;

    private bool isFlying = false;
    private float xRotation = 0f;

    private Rigidbody rb;
    private Vector3 inputVelocity = Vector3.zero;
    private Animator animator;
    private PlayerSkill playerSkill;

    private bool isDashing = false;
    private float dashTimer = 0f;
    private float nextDashTime = 0f;
    private Vector3 dashDirection;
    private float currentDashSpeed = 0f;

    [Header("사운드")]
    private AudioSource audioSource;
    public AudioClip jump_s;
    public AudioClip damage_s;
    public AudioClip die_s;
    public AudioClip eat_s;
    public AudioClip walk_s;
    public AudioClip plop_s;
    public AudioClip land_s;

    private float walkSoundTimer = 0.5f;
    private bool wasGrounded = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
        playerSkill = GetComponent<PlayerSkill>();
        audioSource = GetComponent<AudioSource>();

        if (playerCamera != null)
        {
            initialCameraLocalPos = playerCamera.transform.localPosition;
            initialCameraLocalRot = playerCamera.transform.localRotation;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            currentStamina.Value = maxFlightStamina;
            currentHealth.Value = maxHealth;
            transform.position = new Vector3(0f, 1001f, 0f);
        }

        if (IsOwner)
        {
            playerCamera.gameObject.SetActive(true);
            if (Camera.main != null) Camera.main.gameObject.SetActive(false);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // 1인칭 시점에서 자신의 몸이 화면에 보이지 않도록 그림자만 렌더링
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

            SetUIChildrenActive(true);

            if (staminaSlider != null) { staminaSlider.maxValue = maxFlightStamina; staminaSlider.value = maxFlightStamina; }
            if (healthSlider != null) { healthSlider.maxValue = maxHealth; healthSlider.value = maxHealth; }
            if (satietySlider != null) { satietySlider.maxValue = maxEnergy; satietySlider.value = currentEnergy.Value; }
        }
        else
        {
            playerCamera.gameObject.SetActive(false);
            playerCamera.GetComponent<AudioListener>()?.enabled.Equals(false);
            if (playerUICanvas != null) playerUICanvas.SetActive(false);
        }
    }

    private void Update()
    {
        if (IsOwner)
        {
            float horizontalSpeed = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z).magnitude;

            if (Cursor.lockState == CursorLockMode.None)
            {
                inputVelocity = Vector3.zero;
                return;
            }

            if (isDead.Value)
            {
                HandleSpectatorInput();
                return;
            }

            if (staminaSlider != null) staminaSlider.value = currentStamina.Value;
            if (healthSlider != null) healthSlider.value = currentHealth.Value;
            if (satietySlider != null) satietySlider.value = currentEnergy.Value;

            if (currentStamina.Value <= 0f || isFlightBlocked.Value)
                isFlying = false;

            HandleLook();
            HandleInput();
            SubmitRotationServerRpc(transform.eulerAngles.y);
            HandleAimHighlight();
            HandleEatingProgress();
            HandleInteractInput();

            animator.SetFloat("speed", horizontalSpeed);
            animator.SetBool("flying", isFlying);
            animator.SetBool("dash", isDashing);

            bool grounded = IsGrounded();

            // 이전 프레임에 공중이었다가 이번 프레임에 착지한 경우 착지음 재생
            if (grounded && !wasGrounded && !isFlying && !isDashing && land_s != null)
                audioSource?.PlayOneShot(land_s);
            wasGrounded = grounded;

            // 걷기 효과음을 속도에 비례한 간격으로 재생
            walkSoundTimer -= Time.deltaTime;
            if (!isFlying && !isDashing && grounded && horizontalSpeed > 1f && walk_s != null && walkSoundTimer <= 0f)
            {
                audioSource?.PlayOneShot(walk_s);
                float speedRatio = Mathf.Max(horizontalSpeed / walkSpeed, 1f);
                walkSoundTimer = walk_s.length / speedRatio;
            }
        }
        else
        {
            // 다른 플레이어의 Y축 회전을 서버에서 받아 부드럽게 보간
            float smoothYRot = Mathf.LerpAngle(transform.eulerAngles.y, netYRot.Value, Time.deltaTime * 15f);
            transform.rotation = Quaternion.Euler(0f, smoothYRot, 0f);
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner || !IsSpawned || isDead.Value) return;

        SubmitMovementServerRpc(inputVelocity, isFlying);
    }

    private void LateUpdate()
    {
        // 운반 중인 알을 holdPoint에 매 프레임 붙여놓음
        // LateUpdate를 사용하는 이유: 물리 연산이 끝난 후 위치를 덮어써야 떨림이 없음
        if (carriedEgg != null && holdPoint != null)
        {
            carriedEgg.transform.position = holdPoint.position;
            carriedEgg.transform.rotation = holdPoint.rotation;
        }
    }

    // 거미줄 충돌 등 외부 이벤트로 일정 시간 동안 비행을 차단할 때 서버에서 호출
    public void BlockFlight(float duration)
    {
        if (!IsServer) return;
        isFlightBlocked.Value = true;
        blockTimer = duration;
    }

    // 데미지 처리는 서버에서만 실행하여 체력 조작을 방지
    public void TakeDamage(float damageAmount)
    {
        if (!IsServer) return;

        currentHealth.Value = Mathf.Max(0f, currentHealth.Value - damageAmount);
        PlaySoundClientRpc(PlayerSoundType.Damage);
        Debug.Log($"[{gameObject.name}] 피격. 남은 체력: {currentHealth.Value}");

        if (currentHealth.Value <= 0f) Die();
    }

    private void Die()
    {
        if (isDead.Value) return;

        isDead.Value = true;
        deathCount.Value += 1;
        timeOfDeath.Value = Time.time;

        PlaySoundClientRpc(PlayerSoundType.Die);
        SetDeathStateClientRpc(true);
        Debug.Log($"[{gameObject.name}] 사망. 누적 데스: {deathCount.Value}");

        CheckTeamWipe();
    }

    // 살아있는 플레이어가 한 명도 없으면 강제 게임오버
    private void CheckTeamWipe()
    {
        if (!IsServer) return;

        foreach (PlayerMovement p in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (!p.isDead.Value) return;
        }

        Debug.Log("[GameManager] 전 팀 전멸. 강제 게임오버.");
        GameManager.Instance?.TriggerGameOver();
    }

    [ClientRpc]
    private void SetDeathStateClientRpc(bool deathState)
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            r.enabled = !deathState;

        if (deathState) rb.linearVelocity = Vector3.zero;
        rb.isKinematic = deathState;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = !deathState;

        if (!IsOwner) return;

        SetUIChildrenActive(!deathState);

        if (deathState)
        {
            // 죽어서 UI를 전부 껐지만, 게임 화면과 할당량 정보는 관전 중에도 보여야 함
            gameScreenView?.SetActive(true);
            quotaText?.SetActive(true);

            spectateTarget = null;
        }
    }

    // 서버와 다른 클라이언트(ResurrectionShrine) 모두에서 호출할 수 있도록 RequireOwnership = false
    [ServerRpc(RequireOwnership = false)]
    public void ReviveServerRpc(Vector3 revivePos)
    {
        isDead.Value = false;
        currentHealth.Value = maxHealth;
        ReviveClientRpc(revivePos);
    }

    [ClientRpc]
    private void ReviveClientRpc(Vector3 revivePos)
    {
        if (IsOwner && playerCamera != null)
        {
            playerCamera.transform.localPosition = initialCameraLocalPos;
            playerCamera.transform.localRotation = initialCameraLocalRot;
        }

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            r.enabled = true;

        GetComponent<Collider>()?.enabled.Equals(true);

        // isKinematic을 켰다가 위치를 설정한 뒤 바로 끄면 물리가 개입하지 않아 정확히 이동 가능
        rb.isKinematic = true;
        transform.position = revivePos;
        rb.isKinematic = false;

        if (IsOwner)
        {
            SetUIChildrenActive(true);
            spectateTarget = null;
        }

        Debug.Log($"[{gameObject.name}] 부활 완료.");
    }

    // 마우스 X축으로 캐릭터 전체를 수평 회전, Y축으로 카메라만 수직 회전
    // xRotation을 클램프해서 카메라가 뒤집히지 않도록 제한
    private void HandleLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        xRotation = Mathf.Clamp(xRotation - mouseY, -90f, 90f);
        playerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    private void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (!isFlying && currentStamina.Value > 0f && !isFlightBlocked.Value) isFlying = true;
            else if (isFlying) isFlying = false;

            animator.SetBool("flying", isFlying);
        }

        if (isDashing)
        {
            dashTimer -= Time.deltaTime;
            if (dashTimer <= 0f) isDashing = false;
        }

        if (Input.GetMouseButtonDown(1) && Time.time >= nextDashTime)
        {
            isDashing = true;
            nextDashTime = Time.time + dashCooldown;
            currentDashSpeed = dashSpeed;

            if (isFlying)
            {
                // 비행 중 대시: 카메라가 향하는 방향으로 돌진
                dashDirection = playerCamera.transform.forward;
                dashTimer = flyDashDuration;
            }
            else
            {
                // 지상 대시: 입력 방향 우선, 입력이 없으면 캐릭터 전방으로
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                Vector3 inputDir = (transform.right * h + transform.forward * v).normalized;
                dashDirection = inputDir.magnitude > 0.1f ? inputDir : transform.forward;
                dashTimer = groundDashDuration;
            }
        }

        if (Input.GetKeyDown(KeyCode.Space) && !isFlying && !isDashing && IsGrounded())
            SubmitJumpServerRpc();

        Vector3 baseVelocity = isFlying ? GetFlyVelocity() : GetGroundVelocity();

        // 대시 중에는 baseVelocity를 무시하고 대시 방향으로만 이동
        // 대시가 끝나면 currentDashSpeed를 마찰력으로 감쇠시켜 부드럽게 정지
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

    // 비행 중 후진 입력은 무시 (전방과 좌우만 허용)
    private Vector3 GetFlyVelocity()
    {
        float vertical = Input.GetAxis("Vertical");
        float horizontal = Input.GetAxis("Horizontal");
        Vector3 velocity = Vector3.zero;
        if (vertical > 0) velocity += playerCamera.transform.forward * vertical * flySpeed;
        if (Mathf.Abs(horizontal) > 0.1f) velocity += transform.right * horizontal * flyStrafeSpeed;
        return velocity;
    }

    private Vector3 GetGroundVelocity()
    {
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
        return (transform.right * horizontal + transform.forward * vertical) * currentSpeed;
    }

    private void HandleInteractInput()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;

        if (carriedEgg != null)
        {
            DropEggServerRpc(carriedEgg);
            return;
        }

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance)) return;

        if (hit.collider.CompareTag("StartButton"))
        {
            GameManager.Instance.TargetButtonInteractedServerRpc();
        }
        else if (hit.collider.CompareTag("ReviveButton"))
        {
            FindFirstObjectByType<ResurrectionShrine>()?.InteractReviveButtonServerRpc();
        }
        else if (hit.collider.CompareTag("Egg"))
        {
            NetworkObject eggNetObj = hit.collider.GetComponent<NetworkObject>();
            if (eggNetObj != null) GrabEggServerRpc(eggNetObj);
        }
    }

    private bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, groundCheckDistance);
    }

    [ServerRpc]
    private void SubmitJumpServerRpc()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
    }

    // 이동 연산은 서버에서 처리하여 클라이언트 속도 조작을 방지
    // Unity 기본 중력이 점프 정점 근처에서 부자연스럽게 느껴지는 것을 보완하기 위해
    // 상승 / 하강 구간마다 추가 중력을 적용하는 향상된 중력 모델을 사용
    [ServerRpc]
    private void SubmitMovementServerRpc(Vector3 velocity, bool flyingState)
    {
        if (isDead.Value || rb.isKinematic) return;

        if (isFlightBlocked.Value)
        {
            blockTimer -= Time.fixedDeltaTime;
            if (blockTimer <= 0f) isFlightBlocked.Value = false;
            flyingState = false;
        }

        if (flyingState)
        {
            currentStamina.Value = Mathf.Max(0f, currentStamina.Value - staminaDrainRate * Time.fixedDeltaTime);
            if (currentStamina.Value <= 0f) flyingState = false;
        }
        else if (IsGrounded() && currentStamina.Value < maxFlightStamina)
        {
            currentStamina.Value = Mathf.Min(maxFlightStamina, currentStamina.Value + staminaRegenRate * Time.fixedDeltaTime);
        }

        // 비행 상태가 바뀔 때만 useGravity를 전환하여 불필요한 물리 리셋을 피함
        if (rb.useGravity == flyingState)
        {
            rb.useGravity = !flyingState;
            rb.linearVelocity = flyingState ? Vector3.zero : new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        }

        if (!flyingState)
        {
            // 하강 중: gravity * (fallMultiplier - 1) 만큼 추가 가속 → 묵직하게 낙하
            // 상승 중: gravity * (upwardMultiplier - 1) 만큼 추가 감속 → 빠르게 정점 도달
            if (rb.linearVelocity.y < 0)
                rb.linearVelocity += Vector3.up * Physics.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
            else if (rb.linearVelocity.y > 0)
                rb.linearVelocity += Vector3.up * Physics.gravity.y * (upwardMultiplier - 1f) * Time.fixedDeltaTime;
        }

        float speedMult = playerSkill != null ? playerSkill.speedMultiplier.Value : 1f;

        if (flyingState) rb.linearVelocity = velocity * speedMult;
        else rb.linearVelocity = new Vector3(velocity.x * speedMult, rb.linearVelocity.y, velocity.z * speedMult);
    }

    [ServerRpc]
    private void SubmitRotationServerRpc(float yRot)
    {
        netYRot.Value = yRot;
    }

    // 화면 중앙 레이캐스트 결과에 따라 가리키는 오브젝트의 아웃라인을 켜고 끔
    // 이전에 하이라이트된 오브젝트는 새 오브젝트로 교체되기 전에 먼저 꺼줌
    private void HandleAimHighlight()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance)
            && (hit.collider.CompareTag("Food") || hit.collider.CompareTag("Egg")))
        {
            Outline outline = hit.collider.GetComponent<Outline>();
            if (outline != null && outline != currentlyHighlightedFood)
            {
                DisableCurrentHighlight();
                currentlyHighlightedFood = outline;
                currentlyHighlightedFood.enabled = true;
            }
            return;
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

    // F키를 eatDuration 초 동안 유지해야 섭취 완료 → 도중에 키를 놓으면 타이머 초기화
    private void HandleEatingProgress()
    {
        if (Input.GetKey(KeyCode.F) && currentlyHighlightedFood != null)
        {
            isEating = true;
            eatTimer += Time.deltaTime;

            if (eatTimer >= eatDuration)
            {
                NetworkObject foodNetObj = currentlyHighlightedFood.GetComponent<NetworkObject>();
                if (foodNetObj != null)
                    EatFoodServerRpc(foodNetObj, ShelterZone.IsLocalPlayerInShelter);
                ResetEating();
            }
        }
        else if (isEating)
        {
            ResetEating();
        }
    }

    private void ResetEating()
    {
        isEating = false;
        eatTimer = 0f;
    }

    // 에너지가 maxEnergy에 도달하면 알을 자동 생산
    // 쉘터 안: 공유 재화에 직접 합산 (알 오브젝트 스폰 없음)
    // 쉘터 밖: 알 프리팹을 네트워크 스폰하여 플레이어가 직접 운반해야 함
    [ServerRpc]
    private void EatFoodServerRpc(NetworkObjectReference foodRef, bool isInShelter)
    {
        if (!foodRef.TryGet(out NetworkObject foodNetObj)) return;

        currentEnergy.Value = Mathf.Min(maxEnergy, currentEnergy.Value + 50);
        foodNetObj.Despawn(false);
        PlaySoundClientRpc(PlayerSoundType.Eat);
        Debug.Log($"[Server] 음식 섭취. 에너지: {currentEnergy.Value}");

        if (currentEnergy.Value >= maxEnergy)
        {
            currentEnergy.Value = 0;
            PlaySoundClientRpc(PlayerSoundType.Plop);

            if (isInShelter)
            {
                GameManager.Instance.sharedEggCount.Value += 1;
                Debug.Log($"[Server] 쉘터 내 자동 알 생성. 총: {GameManager.Instance.sharedEggCount.Value}");
            }
            else
            {
                Vector3 spawnPos = transform.position - transform.forward * 0.5f
                    + Vector3.up * 0.2f + Random.insideUnitSphere * 0.1f;
                GameObject newEgg = Instantiate(eggPrefab, spawnPos, Quaternion.identity);
                newEgg.GetComponent<NetworkObject>().Spawn();
            }
        }
    }

    // 마우스 클릭으로 살아있는 팀원을 순서대로 관전
    // 카메라를 관전 대상 뒤편 위쪽으로 이동시켜 3인칭 뷰처럼 보이게 함
    private void HandleSpectatorInput()
    {
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            SwitchSpectateTarget();

        if (spectateTarget == null) return;

        Vector3 targetPos = spectateTarget.position - spectateTarget.forward * 2.5f + Vector3.up * 1.5f;
        playerCamera.transform.position = Vector3.Lerp(playerCamera.transform.position, targetPos, Time.deltaTime * 10f);

        Quaternion targetRot = Quaternion.LookRotation((spectateTarget.position + Vector3.up * 1f) - playerCamera.transform.position);
        playerCamera.transform.rotation = Quaternion.Slerp(playerCamera.transform.rotation, targetRot, Time.deltaTime * 10f);
    }

    // currentSpectateIndex를 순환시켜 살아있는 팀원을 차례대로 관전
    // 살아있는 팀원이 없으면 카메라를 원래 로컬 위치로 되돌림
    private void SwitchSpectateTarget()
    {
        var aliveTeammates = new System.Collections.Generic.List<PlayerMovement>();

        foreach (var p in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (!p.isDead.Value && p != this) aliveTeammates.Add(p);
        }

        if (aliveTeammates.Count > 0)
        {
            currentSpectateIndex = (currentSpectateIndex + 1) % aliveTeammates.Count;
            spectateTarget = aliveTeammates[currentSpectateIndex].transform;
        }
        else
        {
            spectateTarget = null;
            playerCamera.transform.localPosition = initialCameraLocalPos;
            playerCamera.transform.localRotation = initialCameraLocalRot;
        }
    }

    [ClientRpc]
    private void PlaySoundClientRpc(PlayerSoundType sound)
    {
        AudioClip clip = sound switch
        {
            PlayerSoundType.Damage => damage_s,
            PlayerSoundType.Die    => die_s,
            PlayerSoundType.Eat    => eat_s,
            PlayerSoundType.Plop   => plop_s,
            _ => null
        };

        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    // 알을 집을 때 소유권을 이 클라이언트로 이전해야 LateUpdate에서 위치를 제어할 수 있음
    [ServerRpc]
    private void GrabEggServerRpc(NetworkObjectReference eggRef)
    {
        if (!eggRef.TryGet(out NetworkObject eggNetObj)) return;
        // 이미 다른 플레이어가 들고 있는 알은 isKinematic = true 상태이므로 무시
        if (eggNetObj.TryGetComponent(out Rigidbody eggRb) && eggRb.isKinematic) return;

        eggNetObj.ChangeOwnership(OwnerClientId);
        GrabEggClientRpc(eggRef);
    }

    [ClientRpc]
    private void GrabEggClientRpc(NetworkObjectReference eggRef)
    {
        if (!eggRef.TryGet(out NetworkObject eggNetObj)) return;

        if (eggNetObj.TryGetComponent(out Rigidbody eggRb)) eggRb.isKinematic = true;
        if (eggNetObj.TryGetComponent(out Collider col)) col.enabled = false;

        carriedEgg = eggNetObj;
    }

    [ServerRpc]
    private void DropEggServerRpc(NetworkObjectReference eggRef)
    {
        if (!eggRef.TryGet(out NetworkObject eggNetObj)) return;

        eggNetObj.RemoveOwnership();
        DropEggClientRpc(eggRef);
    }

    [ClientRpc]
    private void DropEggClientRpc(NetworkObjectReference eggRef)
    {
        if (!eggRef.TryGet(out NetworkObject eggNetObj)) return;

        carriedEgg = null;

        if (eggNetObj.TryGetComponent(out Collider col)) col.enabled = true;
        if (eggNetObj.TryGetComponent(out Rigidbody eggRb))
        {
            eggRb.isKinematic = false;
            if (IsOwner) eggRb.AddForce(playerCamera.transform.forward * 3f, ForceMode.Impulse);
        }
    }

    // 라운드 시작/종료 시 서버가 모든 플레이어를 특정 위치로 이동시키기 위해 호출
    [ClientRpc]
    public void TeleportClientRpc(Vector3 targetPosition)
    {
        rb.isKinematic = true;
        transform.position = targetPosition + Vector3.up * 1.5f;
        rb.isKinematic = false;
    }

    // playerUICanvas의 모든 자식을 한 번에 활성화/비활성화
    // 사망, 부활, 스폰 시 UI 상태를 일괄 전환하는 데 사용
    private void SetUIChildrenActive(bool active)
    {
        if (playerUICanvas == null) return;
        foreach (Transform child in playerUICanvas.transform)
            child.gameObject.SetActive(active);
    }
}
