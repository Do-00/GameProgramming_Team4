using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class MonsterCatAI : NetworkBehaviour
{
    private AudioSource audioSource;
 
    public AudioClip attackSound;
    public AudioClip jumpSound;
    public AudioClip stunSound;
    public enum CatState
    {
        Idle,
        Staring,
        Preparing,
        Charging,
        Stunned
    }

    [Header("AI 설정")]
    [SerializeField] private float detectionRadius = 70f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private float rotateSpeed = 10f;

    [Header("행동 설정")]
    [SerializeField] private float stareDuration = 1.5f;
    [SerializeField] private float pauseDuration = 0.5f;

    [Header("지상 돌진 설정 (Ground Charge)")]
    [SerializeField] private float pounceInitialSpeed = 40f;
    [SerializeField] private float pounceFriction = 3f;

    [Header("공중 점프 설정 (Aerial Jump)")]
    [SerializeField] private float airAttackThreshold = 2f;
    [SerializeField] private float jumpArcOffset = 1.5f;
    [SerializeField] private float maxJumpHeight = 8f;
    [SerializeField] private float jumpGravityMultiplier = 3f;
    [SerializeField] private float jumpOvershootRatio = 0.2f;

    // ✨ [새로 추가됨] 공격 데미지 설정
    [Header("공격 데미지 설정")]
    [SerializeField] private float dashDamage = 80f;     // 지상 관통 돌진 시 큰 데미지
    [SerializeField] private float airJumpDamage = 40f;  // 공중 점프 관통 시 약한 데미지

    [Header("충돌 및 기절 설정")]
    [SerializeField] private float stunDuration = 5f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("애니메이션")]
    [SerializeField] private Animator catAnimator;
    [SerializeField] private Transform catModel;

    private NetworkVariable<CatState> currentState = new NetworkVariable<CatState>(CatState.Idle);

    private Rigidbody rb;
    private Transform targetPlayer;

    private Vector3 lockedTargetPos;
    private bool isJumpingAttack = false;
    private bool canStunFromCollision = false;

    private void Awake() { rb = GetComponent<Rigidbody>(); audioSource = GetComponent<AudioSource>(); }

    // null 안전 사운드 재생 헬퍼
    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    // 서버에서 호출 → 모든 클라이언트에서 사운드 재생 (0=attack, 1=jump, 2=stun)
    [ClientRpc]
    private void PlaySoundClientRpc(int soundIndex)
    {
        AudioClip clip = soundIndex switch
        {
            0 => attackSound,
            1 => jumpSound,
            2 => stunSound,
            _ => null
        };
        PlaySound(clip);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        currentState.OnValueChanged += OnCatStateChanged;
        StartCoroutine(AILoop());
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) currentState.OnValueChanged -= OnCatStateChanged;
    }

    private IEnumerator AILoop()
    {
        while (true)
        {
            switch (currentState.Value)
            {
                case CatState.Idle: yield return StartCoroutine(HandleIdleState()); break;
                case CatState.Staring: yield return StartCoroutine(HandleStaringState()); break;
                case CatState.Preparing: yield return StartCoroutine(HandlePreparingState()); break;
                case CatState.Charging: yield return StartCoroutine(HandleChargingState()); break;
                case CatState.Stunned: yield return StartCoroutine(HandleStunnedState()); break;
            }
        }
    }

    private IEnumerator HandleIdleState()
    {
        rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);

        while (currentState.Value == CatState.Idle)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius, playerLayer);
            if (hits.Length > 0)
            {
                Transform potentialTarget = hits[0].transform;

                int sightLayerMask = playerLayer | obstacleLayer;

                RaycastHit hit;
                if (Physics.Linecast(transform.position + Vector3.up * 1f, potentialTarget.position, out hit, sightLayerMask))
                {
                    if (hit.collider.CompareTag("Player"))
                    {
                        targetPlayer = potentialTarget;
                        currentState.Value = CatState.Staring;
                        yield break;
                    }
                }
            }
            yield return new WaitForSeconds(0.5f);
        }
    }

    private IEnumerator HandleStaringState()
    {
        rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
        float stateTimer = stareDuration;

        while (stateTimer > 0)
        {
            stateTimer -= Time.deltaTime;

            if (targetPlayer != null)
            {
                Vector3 lookDir = (targetPlayer.position - transform.position).normalized;
                lookDir.y = 0;
                if (lookDir != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotateSpeed);
                }
            }
            yield return null;
        }

        currentState.Value = targetPlayer != null ? CatState.Preparing : CatState.Idle;
    }

    private IEnumerator HandlePreparingState()
    {
        if (targetPlayer == null)
        {
            currentState.Value = CatState.Idle;
            yield break;
        }

        lockedTargetPos = targetPlayer.position;

        float heightDiff = lockedTargetPos.y - transform.position.y;
        if (heightDiff > maxJumpHeight)
        {
            lockedTargetPos.y = transform.position.y + maxJumpHeight;
            heightDiff = maxJumpHeight;
        }

        isJumpingAttack = heightDiff >= airAttackThreshold;

        yield return new WaitForSeconds(pauseDuration);

        currentState.Value = CatState.Charging;
    }

    private IEnumerator HandleChargingState()
    {
        canStunFromCollision = false;

        if (isJumpingAttack)
        {
            PlaySoundClientRpc(1); // jumpSound

            Vector3 displacement = lockedTargetPos - transform.position;
            Vector3 displacementXZ = new Vector3(displacement.x, 0, displacement.z);

            if (displacementXZ.magnitude > 0.1f)
            {
                displacementXZ += displacementXZ * jumpOvershootRatio;
            }

            float h = displacement.y + jumpArcOffset;
            if (h < 2f) h = 2f;
            if (h > maxJumpHeight) h = maxJumpHeight;

            float baseGravity = Mathf.Abs(Physics.gravity.y);
            float jumpGravity = baseGravity * jumpGravityMultiplier;

            float velocityY = Mathf.Sqrt(2 * jumpGravity * h);

            float timeUp = Mathf.Sqrt(2 * h / jumpGravity);
            float fallHeight = Mathf.Max(0, h - displacement.y);
            float timeDown = Mathf.Sqrt(2 * fallHeight / jumpGravity);
            float totalAirTime = timeUp + timeDown;

            Vector3 velocityXZ = displacementXZ / totalAirTime;

            rb.linearVelocity = velocityXZ + Vector3.up * velocityY;

            yield return new WaitForSeconds(0.2f);
            canStunFromCollision = true;

            float timer = totalAirTime;
            while (timer > 0 && currentState.Value == CatState.Charging)
            {
                rb.AddForce(Vector3.down * baseGravity * (jumpGravityMultiplier - 1f), ForceMode.Acceleration);
                timer -= Time.deltaTime;
                yield return null;
            }
        }
        else
        {

            Vector3 groundDir = new Vector3(lockedTargetPos.x - transform.position.x, 0, lockedTargetPos.z - transform.position.z).normalized;
            rb.linearVelocity = groundDir * pounceInitialSpeed;

            yield return new WaitForSeconds(0.1f);
            canStunFromCollision = true;

            float currentSpeed = pounceInitialSpeed;
            while (currentSpeed > 0.1f && currentState.Value == CatState.Charging)
            {
                currentSpeed = Mathf.Lerp(currentSpeed, 0f, Time.deltaTime * pounceFriction);
                rb.linearVelocity = new Vector3(groundDir.x * currentSpeed, rb.linearVelocity.y, groundDir.z * currentSpeed);
                yield return null;
            }
        }

        if (currentState.Value == CatState.Charging)
        {
            currentState.Value = CatState.Stunned;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !canStunFromCollision) return;

        if (currentState.Value == CatState.Charging)
        {
            if (((1 << collision.gameObject.layer) & obstacleLayer) != 0)
            {
                rb.linearVelocity = Vector3.zero;
                currentState.Value = CatState.Stunned;
                Debug.Log("[서버 고양이] 장애물에 충돌하여 기절합니다!");
                PlaySoundClientRpc(2); // stunSound
            }
        }
    }

    // ✨ [새로 추가됨] 플레이어 관통 시 데미지를 주는 트리거 이벤트
    private void OnTriggerEnter(Collider other)
    {
        // 💥 데미지 판정은 서버에서만 처리합니다.
        if (!IsServer) return;

        // 고양이가 돌진(Charging) 상태일 때만 데미지가 들어갑니다.
        if (currentState.Value == CatState.Charging && other.CompareTag("Player"))
        {
            PlayerMovement player = other.GetComponent<PlayerMovement>();

            // 플레이어가 살아있을 때만 타격
            if (player != null && player.currentHealth.Value > 0)
            {
                // 점프 중인지 지상인지 확인하여 데미지 결정
                float currentDamage = isJumpingAttack ? airJumpDamage : dashDamage;

                player.TakeDamage(currentDamage);
                PlaySoundClientRpc(0); // attackSound
                Debug.Log($"[서버 고양이] 관통 공격 성공! 데미지: {currentDamage} (점프 공격: {isJumpingAttack})");
            }
        }
    }

    private IEnumerator HandleStunnedState()
    {
        rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
        yield return new WaitForSeconds(stunDuration);

        targetPlayer = null;
        currentState.Value = CatState.Idle;
    }

    private void OnCatStateChanged(CatState oldState, CatState newState)
    {
        if (catAnimator == null) return;

        if (catModel != null)
        {
            catModel.localRotation = Quaternion.Euler(0, 0, 0);
        }

        switch (newState)
        {
            case CatState.Idle:
                catAnimator.SetInteger("State", 0); // 대기
                break;

            case CatState.Staring:
                catAnimator.SetInteger("State", 2); // 걷기
                break;

            case CatState.Preparing:
                catAnimator.SetInteger("State", 0); // 대기 
                break;

            case CatState.Charging:
                catAnimator.SetInteger("State", 4); // 달리기
                break;

            case CatState.Stunned:
                catAnimator.SetInteger("State", 0); // 대기 
                break;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}