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
    [SerializeField] private float fallMultiplier = 4f; // 낙하 가속도
    [SerializeField] private float upwardMultiplier = 2.5f;  // 상승 가속도
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

    [Header("사망 및 관전 설정")]
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> deathCount = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private int currentSpectateIndex = 0; // 현재 관전 중인 팀원 번호
    private Transform spectateTarget;     // 관전할 대상의 위치

    [Header("체력 설정")]
    [SerializeField] private float maxHealth = 100f; // 최대 체력

    [Header("UI 설정")]
    [SerializeField] private GameObject playerUICanvas; // 내 화면에서만 켤 캔버스
    [SerializeField] private Slider staminaSlider;      // 스태미너 바 슬라이더
    [SerializeField] private Slider healthSlider;       // 체력 바 슬라이더
    [SerializeField] private Slider satietySlider;      // 포만감 바 슬라이더

    // 서버가 엄격하게 관리하고 클라이언트들에게 실시간 복제할 네트워크 변수들
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
    [SerializeField] private int maxEnergy = 100; // 최대 포만감(에너지) 제한치
    [SerializeField] private GameObject eggPrefab;
    [SerializeField] private float interactDistance = 4f; // 음식 섭취 최대 사거리
    [SerializeField] private float eatDuration = 3f; // 섭취에 걸리는 시간
    private Outline currentlyHighlightedFood; // 현재 하이라이트 중인 음식
    private float eatTimer = 0f;                     // 현재 얼마나 먹었는지 측정
    private bool isEating = false;                   // 현재 먹고 있는 중인지

    [Header("알 운반 설정")]
    [SerializeField] private Transform holdPoint; // 카메라의 자식으로 둔 빈 오브젝트 (알을 들 위치)
    private NetworkObject carriedEgg = null;      // 현재 들고 있는 알

    private bool isFlying = false;  // 날고 있는지 여부 (서버가 관리)
    private float xRotation = 0f;  // 카메라의 수직 회전값 (서버가 관리)

    private Rigidbody rb;
    private Vector3 inputVelocity = Vector3.zero;
    private Animator animator; //애니메이션 
    private PlayerSkill playerSkill; // 플레이어 스킬

    private bool isDashing = false;   // 대쉬 중인지 여부 (서버가 관리)
    private float dashTimer = 0f;     // 대쉬 지속 시간 타이머
    private float nextDashTime = 0f;  // 다음 대쉬 가능 시간
    private Vector3 dashDirection;    // 대쉬 방향 (서버가 관리)
    private float currentDashSpeed = 0f;  // 현재 대쉬 속도 (서버가 관리)

    [Header("사운드")]
    private AudioSource audioSource;
    private AudioSource flyAudioSource;   // 비행 루프 전용 AudioSource
    public AudioClip jump_s;
    public AudioClip damage_s;
    public AudioClip die_s;
    public AudioClip eat_s;
    public AudioClip walk_s;
    public AudioClip plop_s;
    public AudioClip land_s;              // 착지 사운드
    public AudioClip flyStart_s;          // 비행 모드 전환 사운드
    public AudioClip flyLoop_s;           // 비행 중 이동 바람 사운드 (루프)
    public AudioClip webStruggle_s;       // 거미줄에 걸린 상태에서 이동 시 사운드
    private float walkSoundTimer = 0.5f;    // 발소리 간격 타이머
    private float webStruggleSoundTimer = 0f; // 거미줄 발버둥 소리 간격 타이머
    private bool wasGrounded = true;      // 착지 감지용: 이전 프레임 지면 여부

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
        playerSkill = GetComponent<PlayerSkill>();
        audioSource = GetComponent<AudioSource>();

        flyAudioSource = gameObject.AddComponent<AudioSource>();
        flyAudioSource.loop = true;
        flyAudioSource.playOnAwake = false;
        flyAudioSource.spatialBlend = 0f;
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

            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }

            if (playerUICanvas != null) playerUICanvas.SetActive(true);
            if (staminaSlider != null)
            {
                staminaSlider.maxValue = maxFlightStamina;
                staminaSlider.value = maxFlightStamina;
            }
            if (healthSlider != null)
            {
                healthSlider.maxValue = maxHealth;
                healthSlider.value = maxHealth;
            }
            if (satietySlider != null)
            {
                satietySlider.maxValue = maxEnergy;
                satietySlider.value = currentEnergy.Value;
            }
        }
        else
        {
            playerCamera.gameObject.SetActive(false);
            if (playerCamera.GetComponent<AudioListener>() != null)
                playerCamera.GetComponent<AudioListener>().enabled = false;

            if (playerUICanvas != null) playerUICanvas.SetActive(false);
        }
    }

    void Update()
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
            {
                isFlying = false;
            }

            HandleLook();
            HandleInput();
            SubmitRotationServerRpc(transform.eulerAngles.y);
            HandleAimHighlight();
            HandleEatingProgress();

            if (Input.GetKeyDown(KeyCode.E))
            {
                if (carriedEgg != null)
                {
                    DropEggServerRpc(carriedEgg);
                    return;
                }

                Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
                {
                    if (hit.collider.CompareTag("StartButton"))
                    {
                        GameManager.Instance.TargetButtonInteractedServerRpc();
                        return;
                    }
                    else if (hit.collider.CompareTag("Egg"))
                    {
                        NetworkObject eggNetObj = hit.collider.GetComponent<NetworkObject>();
                        if (eggNetObj != null)
                        {
                            GrabEggServerRpc(eggNetObj);
                            return;
                        }
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                TakeDamage(20f);
            }

            animator.SetFloat("speed", horizontalSpeed);
            animator.SetBool("flying", isFlying);
            animator.SetBool("dash", isDashing);

            bool grounded = IsGrounded();

            if (grounded && !wasGrounded && !isFlying && !isDashing && audioSource != null && land_s != null)
            {
                audioSource.PlayOneShot(land_s);
            }
            wasGrounded = grounded;

            walkSoundTimer -= Time.deltaTime;
            if (!isFlying && !isDashing && grounded && horizontalSpeed > 1f
                && audioSource != null && walk_s != null && walkSoundTimer <= 0f)
            {
                audioSource.PlayOneShot(walk_s);
                float speedRatio = Mathf.Max(horizontalSpeed / walkSpeed, 1f);
                walkSoundTimer = walk_s.length / speedRatio;
            }

            // 비행 중 이동 바람 소리 루프 관리
            if (flyAudioSource != null && flyLoop_s != null)
            {
                bool shouldPlayFlyLoop = isFlying && inputVelocity.magnitude > 0.1f;
                if (shouldPlayFlyLoop && !flyAudioSource.isPlaying)
                {
                    flyAudioSource.clip = flyLoop_s;
                    flyAudioSource.Play();
                }
                else if (!shouldPlayFlyLoop && flyAudioSource.isPlaying)
                {
                    flyAudioSource.Stop();
                }
            }

            // 거미줄에 걸린 상태에서 이동할 때 발버둥 소리
            webStruggleSoundTimer -= Time.deltaTime;
            if (isFlightBlocked.Value && horizontalSpeed > 0.5f
                && audioSource != null && webStruggle_s != null && webStruggleSoundTimer <= 0f)
            {
                audioSource.PlayOneShot(webStruggle_s);
                webStruggleSoundTimer = webStruggle_s.length;
            }
        }
        else
        {
            float smoothYRot = Mathf.LerpAngle(transform.eulerAngles.y, netYRot.Value, Time.deltaTime * 15f);
            transform.rotation = Quaternion.Euler(0f, smoothYRot, 0f);
        }
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;
        if (isDead.Value) return;

        SubmitMovementServerRpc(inputVelocity, isFlying);
    }

    public void BlockFlight(float duration)
    {
        if (!IsServer) return;
        isFlightBlocked.Value = true;
        blockTimer = duration;
    }

    public void TakeDamage(float damageAmount)
    {
        if (!IsServer) return;

        currentHealth.Value = Mathf.Max(0f, currentHealth.Value - damageAmount);
        PlaySoundClientRpc("damage");
        Debug.Log($"[{gameObject.name}] 맞았습니다! 남은 체력: {currentHealth.Value}");

        if (currentHealth.Value <= 0f) Die();
    }

    private void Die()
    {
        if (isDead.Value) return;

        isDead.Value = true;
        deathCount.Value += 1;
        PlaySoundClientRpc("die");

        SetDeathStateClientRpc(true);
        Debug.Log($"[{gameObject.name}] 파리 사망! 누적 데스: {deathCount.Value}");
    }

    [ClientRpc]
    private void SetDeathStateClientRpc(bool deathState)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers) r.enabled = !deathState;

        if (deathState) rb.linearVelocity = Vector3.zero;
        rb.isKinematic = deathState;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = !deathState;

        if (IsOwner)
        {
            if (playerUICanvas != null) playerUICanvas.SetActive(!deathState);

            if (deathState)
            {
                spectateTarget = this.transform;
                Debug.Log("[시스템] 사망. 마우스 좌클릭으로 살아있는 팀원을 관전.");
            }
        }
    }

    private void HandleLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        playerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        transform.Rotate(Vector3.up * mouseX);
    }

    private void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (!isFlying && currentStamina.Value > 0f && !isFlightBlocked.Value)
            {
                isFlying = true;
                if (audioSource != null && flyStart_s != null)
                    audioSource.PlayOneShot(flyStart_s);
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

    private bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, groundCheckDistance);
    }

    [ServerRpc]
    private void SubmitJumpServerRpc()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
    }

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
            currentStamina.Value = Mathf.Max(0f, currentStamina.Value - (staminaDrainRate * Time.fixedDeltaTime));
            if (currentStamina.Value <= 0f) flyingState = false;
        }
        else if (Physics.Raycast(transform.position, Vector3.down, groundCheckDistance))
        {
            if (currentStamina.Value < maxFlightStamina)
            {
                currentStamina.Value = Mathf.Min(maxFlightStamina, currentStamina.Value + (staminaRegenRate * Time.fixedDeltaTime));
            }
        }

        if (rb.useGravity == flyingState)
        {
            rb.useGravity = !flyingState;
            if (flyingState) rb.linearVelocity = Vector3.zero;
            else rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        }

        // ✨ [다시 추가된 중력 가속도 보정 로직]
        if (!flyingState)
        {
            // 떨어질 때 더 빨리 떨어지도록 (fallMultiplier 적용)
            if (rb.linearVelocity.y < 0)
            {
                rb.linearVelocity += Vector3.up * Physics.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
            }
            // 상승 중일 때도 묵직함을 주기 위해 가속도 보정 (upwardMultiplier 적용)
            else if (rb.linearVelocity.y > 0)
            {
                rb.linearVelocity += Vector3.up * Physics.gravity.y * (upwardMultiplier - 1f) * Time.fixedDeltaTime;
            }
        }

        float mult = (playerSkill != null) ? playerSkill.speedMultiplier.Value : 1f;

        if (flyingState) rb.linearVelocity = velocity * mult;
        else rb.linearVelocity = new Vector3(velocity.x * mult, rb.linearVelocity.y, velocity.z * mult);
    }

    [ServerRpc]
    private void SubmitRotationServerRpc(float yRot)
    {
        netYRot.Value = yRot;
    }

    private void HandleAimHighlight()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            if (hit.collider.CompareTag("Food") || hit.collider.CompareTag("Egg"))
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

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            if (hit.collider.CompareTag("Food"))
            {
                NetworkObject foodNetObj = hit.collider.GetComponent<NetworkObject>();
                if (foodNetObj != null)
                {
                    EatFoodServerRpc(foodNetObj, ShelterZone.IsLocalPlayerInShelter);
                }
            }
        }
    }

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
                {
                    EatFoodServerRpc(foodNetObj, ShelterZone.IsLocalPlayerInShelter);
                }
                ResetEating();
            }
        }
        else
        {
            if (isEating) ResetEating();
        }
    }

    private void ResetEating()
    {
        isEating = false;
        eatTimer = 0f;
    }

    [ServerRpc]
    private void EatFoodServerRpc(NetworkObjectReference foodRef, bool isInShelter)
    {
        if (foodRef.TryGet(out NetworkObject foodNetObj))
        {
            if (IsOwner) DisableCurrentHighlight();

            currentEnergy.Value = Mathf.Min(maxEnergy, currentEnergy.Value + 50);
            foodNetObj.Despawn(false);
            PlaySoundClientRpc("eat");
            Debug.Log("[Server] Food eaten! Energy: " + currentEnergy.Value);

            if (currentEnergy.Value >= maxEnergy)
            {
                currentEnergy.Value = 0;
                PlaySoundClientRpc("plop");

                if (isInShelter)
                {
                    GameManager.Instance.sharedEggCount.Value += 1;
                    Debug.Log($"[서버] 쉘터 안에서 자동 알 생성(화폐) +1 | 총: {GameManager.Instance.sharedEggCount.Value}");
                }
                else
                {
                    Vector3 spawnPos = transform.position - transform.forward * 0.5f
                        + Vector3.up * 0.2f + Random.insideUnitSphere * 0.1f;
                    GameObject newEgg = Instantiate(eggPrefab, spawnPos, Quaternion.identity);
                    newEgg.GetComponent<NetworkObject>().Spawn();
                    Debug.Log("[서버] 밖에서 일반 알 자동 스폰 완료");
                }
            }
        }
    }

    private void HandleSpectatorInput()
    {
        if (Input.GetMouseButtonDown(0)) SwitchSpectateTarget();

        if (spectateTarget != null)
        {
            playerCamera.transform.position = spectateTarget.position;
            playerCamera.transform.rotation = spectateTarget.rotation;
        }
    }

    private void SwitchSpectateTarget()
    {
        PlayerMovement[] allPlayers = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        System.Collections.Generic.List<PlayerMovement> aliveTeammates = new System.Collections.Generic.List<PlayerMovement>();

        foreach (var p in allPlayers)
        {
            if (!p.isDead.Value && p != this) aliveTeammates.Add(p);
        }

        if (aliveTeammates.Count > 0)
        {
            currentSpectateIndex = (currentSpectateIndex + 1) % aliveTeammates.Count;
            spectateTarget = aliveTeammates[currentSpectateIndex].playerCamera.transform;
        }
        else
        {
            spectateTarget = this.transform;
            Debug.Log("살아있는 팀원이 없음... 게임 오버 대기 중.");
        }
    }

    [ClientRpc]
    private void PlaySoundClientRpc(string clipName)
    {
        AudioClip clip = clipName switch
        {
            "damage" => damage_s,
            "die" => die_s,
            "eat" => eat_s,
            "plop" => plop_s,
            _ => null
        };
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    [ServerRpc]
    private void GrabEggServerRpc(NetworkObjectReference eggRef)
    {
        if (eggRef.TryGet(out NetworkObject eggNetObj))
        {
            if (eggNetObj.TryGetComponent(out Rigidbody rb) && rb.isKinematic) return;

            eggNetObj.ChangeOwnership(OwnerClientId);
            GrabEggClientRpc(eggRef);
        }
    }

    [ClientRpc]
    private void GrabEggClientRpc(NetworkObjectReference eggRef)
    {
        if (eggRef.TryGet(out NetworkObject eggNetObj))
        {
            if (eggNetObj.TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
            if (eggNetObj.TryGetComponent(out Collider col)) col.enabled = false;

            carriedEgg = eggNetObj;
        }
    }

    [ServerRpc]
    private void DropEggServerRpc(NetworkObjectReference eggRef)
    {
        if (eggRef.TryGet(out NetworkObject eggNetObj))
        {
            eggNetObj.RemoveOwnership();
            DropEggClientRpc(eggRef);
        }
    }

    [ClientRpc]
    private void DropEggClientRpc(NetworkObjectReference eggRef)
    {
        if (eggRef.TryGet(out NetworkObject eggNetObj))
        {
            carriedEgg = null;

            if (eggNetObj.TryGetComponent(out Collider col)) col.enabled = true;
            if (eggNetObj.TryGetComponent(out Rigidbody rb))
            {
                rb.isKinematic = false;
                if (IsOwner) rb.AddForce(playerCamera.transform.forward * 3f, ForceMode.Impulse);
            }
        }
    }

    private void LateUpdate()
    {
        if (carriedEgg != null && holdPoint != null)
        {
            carriedEgg.transform.position = holdPoint.position;
            carriedEgg.transform.rotation = holdPoint.rotation;
        }
    }

    [ClientRpc]
    public void TeleportClientRpc(Vector3 targetPosition)
    {
        rb.isKinematic = true;
        transform.position = targetPosition + Vector3.up * 1.5f;
        rb.isKinematic = false;
    }
}