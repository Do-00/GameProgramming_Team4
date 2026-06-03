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
    [SerializeField] private float fallMultiplier = 4f; // 낙하 가속도                   -------- 새로 추가함
    [SerializeField] private float upwardMultiplier = 2.5f;  // 상승 가속도                -------- 새로 추가함
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


    private PlayerSkill playerSkill;


    private void Awake()
    {
        rb = GetComponent<Rigidbody>(); // Rigidbody 컴포넌트를 가져옴
        animator = GetComponent<Animator>(); //Animator 컴포넌트 가져옴
        playerSkill = GetComponent<PlayerSkill>();

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


            // 내 화면의 UI와 슬라이더 기본값 초기화 세팅
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


            if (staminaSlider != null)
            {
                staminaSlider.value = currentStamina.Value;
            }
            if (healthSlider != null)
            {
                healthSlider.value = currentHealth.Value;
            }
            if (satietySlider != null)
            {
                satietySlider.value = currentEnergy.Value;
            }

            if (currentStamina.Value <= 0f || isFlightBlocked.Value)
            {
                isFlying = false;
            }

            HandleLook();
            HandleInput();
            SubmitRotationServerRpc(transform.eulerAngles.y);
            HandleAimHighlight();
            HandleEatingProgress();


            // E 키 상호작용 (버튼 누르기 or 알 낳기)
            if (Input.GetKeyDown(KeyCode.E))
            {
                // 먼저 광선을 쏴서 시작 버튼을 바라보고 있는지 확인
                Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
                {
                    if (hit.collider.CompareTag("StartButton"))
                    {
                        // 게임 시작 버튼을 눌렀다면 서버에 시작 명령을 내리고 리턴(알 낳기 취소)
                        GameManager.Instance.StartGameServerRpc();
                        return;
                    }
                }

                // 버튼을 바라본 게 아니라면 기존처럼 에너지를 확인하고 알을 낳음
                if (currentEnergy.Value >= 1)
                {
                    LayEggsServerRpc();
                }
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                TakeDamage(20f);
            }

            animator.SetFloat("speed", horizontalSpeed);
            animator.SetBool("flying", isFlying);
            animator.SetBool("dash", isDashing);

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

        // [수정됨 1번] 중복 호출을 제거하고, 죽었을 때 물리 이동 명령을 보내지 않도록 수정!
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
        Debug.Log($"[{gameObject.name}] 맞았습니다! 남은 체력: {currentHealth.Value}");

        if (currentHealth.Value <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        if (isDead.Value) return;

        isDead.Value = true;
        deathCount.Value += 1;

        SetDeathStateClientRpc(true);

        Debug.Log($"[{gameObject.name}] 파리 사망! 누적 데스: {deathCount.Value}");
    }

    [ClientRpc]
    private void SetDeathStateClientRpc(bool deathState)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers) r.enabled = !deathState;

        if (deathState)
        {
            rb.linearVelocity = Vector3.zero;
        }
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
        // [수정됨 2번] 방어막을 밖으로 꺼내서 가장 윗줄에 배치하여 완벽하게 방어!
        if (isDead.Value || rb.isKinematic) return;

        if (isFlightBlocked.Value)
        {
            blockTimer -= Time.fixedDeltaTime;
            if (blockTimer <= 0f)
            {
                isFlightBlocked.Value = false;
            }
            flyingState = false;
        }

        if (flyingState)
        {
            currentStamina.Value = Mathf.Max(0f, currentStamina.Value - (staminaDrainRate * Time.fixedDeltaTime));
            if (currentStamina.Value <= 0f)
            {
                flyingState = false;
            }
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

        float mult = (playerSkill != null) ? playerSkill.speedMultiplier.Value : 1f;

        if (flyingState)
        {
            rb.linearVelocity = velocity * mult;
        }
        else
        {
            rb.linearVelocity = new Vector3(velocity.x * mult, rb.linearVelocity.y, velocity.z * mult);

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
    private void SubmitRotationServerRpc(float yRot)
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
                    DisableCurrentHighlight();
                    currentlyHighlightedFood = foodOutline;
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


            currentEnergy.Value = Mathf.Min(maxEnergy, currentEnergy.Value + 10);
            foodNetObj.Despawn(false);


            Debug.Log("[서버] 음식을 섭취했습니다! 에너지: " + currentEnergy.Value);
        }
    }

    [ServerRpc]
    private void LayEggsServerRpc()
    {
        if (currentEnergy.Value >= 1 && eggPrefab != null)
        {

            currentEnergy.Value -= 1;
            Vector3 spawnPos = transform.position - transform.forward * 0.5f + Vector3.up * 0.2f + Random.insideUnitSphere * 0.1f;
            GameObject newEgg = Instantiate(eggPrefab, spawnPos, Quaternion.identity);
            newEgg.GetComponent<NetworkObject>().Spawn();

            Debug.Log($"[서버] 알을 1개 낳았습니다. 남은 에너지: {currentEnergy.Value}");
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
                    EatFoodServerRpc(foodNetObj);
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

    private void HandleSpectatorInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            SwitchSpectateTarget();
        }

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
            if (!p.isDead.Value && p != this)
            {
                aliveTeammates.Add(p);
            }
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
    public void TeleportClientRpc(Vector3 targetPosition)
    {
        // 물리 엔진(Rigidbody)이 켜져 있으면 강제 이동 시 튕겨 나갈 수 있으므로 잠깐 끕니다.
        rb.isKinematic = true;

        // 바닥에 파고들지 않도록 Y축으로 살짝 위(예: 1.5f)로 띄워서 텔레포트
        transform.position = targetPosition + Vector3.up * 1.5f;

        // 이동 완료 후 다시 물리 엔진 가동
        rb.isKinematic = false;
    }

}
