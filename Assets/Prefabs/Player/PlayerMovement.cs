using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("이동 설정")]
    [SerializeField] private float walkSpeed = 10f;
    [SerializeField] private float sprintSpeed = 15f;
    [SerializeField] private float flySpeed = 20f;
    [SerializeField] private float flyStrafeSpeed = 12f;

    [Header("점프 & 대쉬 설정")]
    [SerializeField] private float jumpForce = 15f;
    [SerializeField] private float fallMultiplier = 4f;
    [SerializeField] private float upwardMultiplier = 2.5f;
    [SerializeField] private float dashSpeed = 40f;
    [SerializeField] private float groundDashDuration = 0.2f;
    [SerializeField] private float flyDashDuration = 0.15f;
    [SerializeField] private float dashCooldown = 1.5f;
    [SerializeField] private float dashFriction = 5f;
    [SerializeField] private float groundCheckDistance = 1.1f;

    [Header("비행 스태미너 설정")]
    [SerializeField] private float maxFlightStamina = 20f;
    [SerializeField] private float staminaDrainRate = 1f;
    [SerializeField] private float staminaRegenRate = 1.5f;

    [Header("사망 및 관전 설정")]
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> deathCount = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private int currentSpectateIndex = 0;
    private Transform spectateTarget;

    [Header("부활 시스템")]
    public NetworkVariable<float> timeOfDeath = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private Vector3 initialCameraLocalPos;
    private Quaternion initialCameraLocalRot;

    [Header("체력 설정")]
    [SerializeField] private float maxHealth = 100f;

    [Header("UI 설정")]
    [SerializeField] private GameObject playerUICanvas;
    [SerializeField] private GameObject GameScreenView;
    [SerializeField] private GameObject QuotaText;
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
    [SerializeField] private float eatDuration = 3f;
    private Outline currentlyHighlightedFood; 
    private float eatTimer = 0f;                     
    private bool isEating = false;                   

    [Header("알 운반 설정")]
    [SerializeField] private Transform holdPoint; 
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

            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }

            if (playerUICanvas != null) 
            {
                playerUICanvas.SetActive(true);
                // 혹시 꺼져있을 자식 UI들도 초기화 시 켜줍니다.
                foreach (Transform child in playerUICanvas.transform)
                {
                    child.gameObject.SetActive(true);
                }
            }

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

            // 남의 화면 UI 캔버스는 통째로 꺼두는 것이 맞습니다.
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
                    else if (hit.collider.CompareTag("ReviveButton"))
                    {
                        ResurrectionShrine shrine = FindFirstObjectByType<ResurrectionShrine>();
                        if (shrine != null) shrine.InteractReviveButtonServerRpc();
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
        if (!IsSpawned) return;
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
        timeOfDeath.Value = Time.time; 

        PlaySoundClientRpc("die");
        SetDeathStateClientRpc(true);
        Debug.Log($"[{gameObject.name}] 파리 사망! 누적 데스: {deathCount.Value}");

        CheckTeamWipe();
    }

    private void CheckTeamWipe()
    {
        if (!IsServer) return;

        PlayerMovement[] allPlayers = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        foreach (PlayerMovement p in allPlayers)
        {
            if (!p.isDead.Value) return;
        }

        Debug.Log("[시스템] 모든 파리가 사망했습니다. 강제 게임 오버를 진행합니다.");

        if (GameManager.Instance != null)
        {
            GameManager.Instance.TriggerGameOver();
        }
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
            if (playerUICanvas != null)
            {
                if (deathState)
                {
                    foreach (Transform child in playerUICanvas.transform)
                    {
                        child.gameObject.SetActive(false);
                    }
                    // 2. 관전 중 유지할 UI만 다시 활성화
                    if (GameScreenView != null) GameScreenView.SetActive(true);
                    if (QuotaText != null) QuotaText.SetActive(true);
                }
                else
                {
                    foreach (Transform child in playerUICanvas.transform)
                    {
                        child.gameObject.SetActive(true);
                    }
                }
            }

            if (deathState)
            {
                spectateTarget = null;
                Debug.Log("[시스템] 사망. 마우스 클릭으로 살아있는 팀원을 관전.");
            }
        }
    }

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

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers) r.enabled = true;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        rb.isKinematic = true;
        transform.position = revivePos;
        rb.isKinematic = false;

        if (IsOwner)
        {
            // ✨ [수정됨] 부활 시 캔버스 안의 자식(UI 요소들)을 다시 켜줍니다.
            if (playerUICanvas != null)
            {
                foreach (Transform child in playerUICanvas.transform)
                {
                    child.gameObject.SetActive(true);
                }
            }
            spectateTarget = null;
        }

        Debug.Log($"[{gameObject.name}] 부활 완료!");
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
            if (!isFlying && currentStamina.Value > 0f && !isFlightBlocked.Value) isFlying = true;
            else if (isFlying) isFlying = false;

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

        if (!flyingState)
        {
            if (rb.linearVelocity.y < 0)
            {
                rb.linearVelocity += Vector3.up * Physics.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
            }
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
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) SwitchSpectateTarget();

        if (spectateTarget != null)
        {
            Vector3 targetPos = spectateTarget.position - spectateTarget.forward * 2.5f + Vector3.up * 1.5f;
            
            playerCamera.transform.position = Vector3.Lerp(playerCamera.transform.position, targetPos, Time.deltaTime * 10f);
            
            Quaternion targetRot = Quaternion.LookRotation((spectateTarget.position + Vector3.up * 1f) - playerCamera.transform.position);
            playerCamera.transform.rotation = Quaternion.Slerp(playerCamera.transform.rotation, targetRot, Time.deltaTime * 10f);
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
            spectateTarget = aliveTeammates[currentSpectateIndex].transform;
        }
        else
        {
            spectateTarget = null;
            playerCamera.transform.localPosition = initialCameraLocalPos;
            playerCamera.transform.localRotation = initialCameraLocalRot;
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