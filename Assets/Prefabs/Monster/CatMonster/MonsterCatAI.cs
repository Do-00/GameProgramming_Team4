using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class MonsterCatAI : NetworkBehaviour
{
    private enum CatSoundType { Attack = 0, Jump = 1, Stun = 2 }

    public enum CatState { Idle, Staring, Preparing, Charging, Stunned }

    [Header("AI 설정")]
    [SerializeField] private float detectionRadius = 70f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private float rotateSpeed = 10f;

    [Header("행동 설정")]
    [SerializeField] private float stareDuration = 1.5f;
    [SerializeField] private float pauseDuration = 0.5f;

    [Header("지상 돌진 설정")]
    [SerializeField] private float pounceInitialSpeed = 40f;
    [SerializeField] private float pounceFriction = 3f;

    [Header("공중 점프 설정")]
    [SerializeField] private float airAttackThreshold = 2f;
    [SerializeField] private float jumpArcOffset = 1.5f;
    [SerializeField] private float maxJumpHeight = 8f;
    [SerializeField] private float jumpGravityMultiplier = 3f;
    [SerializeField] private float jumpOvershootRatio = 0.2f;

    [Header("공격 데미지 설정")]
    [SerializeField] private float dashDamage = 80f;
    [SerializeField] private float airJumpDamage = 40f;

    [Header("충돌 & 기절 설정")]
    [SerializeField] private float stunDuration = 5f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("애니메이션")]
    [SerializeField] private Animator catAnimator;
    [SerializeField] private Transform catModel;

    [Header("조명 & 안광 설정")]
    [SerializeField] private Light catEyeLight;
    [SerializeField] private GameObject glowEffect;
    [SerializeField] private Color chargeLightColor = Color.red;

    private NetworkVariable<CatState> currentState = new NetworkVariable<CatState>(CatState.Idle);

    private Rigidbody rb;
    private Transform targetPlayer;
    private Vector3 lockedTargetPos;
    private bool isJumpingAttack = false;
    private bool canStunFromCollision = false;

    private AudioSource audioSource;
    public AudioClip attackSound;
    public AudioClip jumpSound;
    public AudioClip stunSound;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        audioSource = GetComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        currentState.OnValueChanged += OnCatStateChanged;
        UpdateCatLight(false);

        if (!IsServer) return;
        StartCoroutine(AILoop());
    }

    public override void OnNetworkDespawn()
    {
        currentState.OnValueChanged -= OnCatStateChanged;
    }

    private IEnumerator AILoop()
    {
        while (true)
        {
            switch (currentState.Value)
            {
                case CatState.Idle:      yield return StartCoroutine(HandleIdleState());      break;
                case CatState.Staring:   yield return StartCoroutine(HandleStaringState());   break;
                case CatState.Preparing: yield return StartCoroutine(HandlePreparingState()); break;
                case CatState.Charging:  yield return StartCoroutine(HandleChargingState());  break;
                case CatState.Stunned:   yield return StartCoroutine(HandleStunnedState());   break;
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
                int sightMask = playerLayer | obstacleLayer;

                if (Physics.Linecast(transform.position + Vector3.up * 1f, potentialTarget.position, out RaycastHit hit, sightMask)
                    && hit.collider.CompareTag("Player"))
                {
                    targetPlayer = potentialTarget;
                    currentState.Value = CatState.Staring;
                    yield break;
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
            PlaySoundClientRpc(CatSoundType.Jump);

            Vector3 displacement = lockedTargetPos - transform.position;
            Vector3 displacementXZ = new Vector3(displacement.x, 0, displacement.z);

            if (displacementXZ.magnitude > 0.1f)
                displacementXZ += displacementXZ * jumpOvershootRatio;

            float h = Mathf.Clamp(displacement.y + jumpArcOffset, 2f, maxJumpHeight);
            float baseGravity = Mathf.Abs(Physics.gravity.y);
            float jumpGravity = baseGravity * jumpGravityMultiplier;

            float velocityY = Mathf.Sqrt(2 * jumpGravity * h);
            float timeUp = Mathf.Sqrt(2 * h / jumpGravity);
            float fallHeight = Mathf.Max(0, h - displacement.y);
            float timeDown = Mathf.Sqrt(2 * fallHeight / jumpGravity);
            float totalAirTime = timeUp + timeDown;

            rb.linearVelocity = displacementXZ / totalAirTime + Vector3.up * velocityY;

            yield return new WaitForSeconds(0.2f);
            canStunFromCollision = true;

            float timer = totalAirTime;
            while (timer > 0 && currentState.Value == CatState.Charging)
            {
                rb.AddForce(Vector3.down * baseGravity * (jumpGravityMultiplier - 1f), ForceMode.Acceleration);
                timer -= Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
        }
        else
        {
            Vector3 groundDir = new Vector3(
                lockedTargetPos.x - transform.position.x,
                0,
                lockedTargetPos.z - transform.position.z).normalized;

            rb.linearVelocity = groundDir * pounceInitialSpeed;

            yield return new WaitForSeconds(0.1f);
            canStunFromCollision = true;

            float currentSpeed = pounceInitialSpeed;
            while (currentSpeed > 0.1f && currentState.Value == CatState.Charging)
            {
                currentSpeed = Mathf.Lerp(currentSpeed, 0f, Time.fixedDeltaTime * pounceFriction);
                rb.linearVelocity = new Vector3(groundDir.x * currentSpeed, rb.linearVelocity.y, groundDir.z * currentSpeed);
                yield return new WaitForFixedUpdate();
            }
        }

        if (currentState.Value == CatState.Charging)
            currentState.Value = CatState.Stunned;
    }

    private IEnumerator HandleStunnedState()
    {
        rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
        yield return new WaitForSeconds(stunDuration);

        targetPlayer = null;
        currentState.Value = CatState.Idle;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !canStunFromCollision) return;
        if (currentState.Value != CatState.Charging) return;

        if (((1 << collision.gameObject.layer) & obstacleLayer) != 0)
        {
            rb.linearVelocity = Vector3.zero;
            currentState.Value = CatState.Stunned;
            PlaySoundClientRpc(CatSoundType.Stun);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (currentState.Value != CatState.Charging || !other.CompareTag("Player")) return;

        PlayerMovement player = other.GetComponent<PlayerMovement>();
        if (player != null && player.currentHealth.Value > 0)
        {
            float damage = isJumpingAttack ? airJumpDamage : dashDamage;
            player.TakeDamage(damage);
            PlaySoundClientRpc(CatSoundType.Attack);
        }
    }

    private void UpdateCatLight(bool turnOn)
    {
        if (catEyeLight != null)
        {
            catEyeLight.enabled = turnOn;
            if (turnOn) catEyeLight.color = chargeLightColor;
        }

        glowEffect?.SetActive(turnOn);
    }

    private void OnCatStateChanged(CatState oldState, CatState newState)
    {
        bool isAggressive = newState == CatState.Staring
            || newState == CatState.Preparing
            || newState == CatState.Charging;

        UpdateCatLight(isAggressive);

        if (catAnimator == null) return;

        catModel?.localRotation.Equals(Quaternion.identity);

        int animState = newState switch
        {
            CatState.Staring  => 2,
            CatState.Charging => 4,
            _                 => 0
        };
        catAnimator.SetInteger("State", animState);
    }

    [ClientRpc]
    private void PlaySoundClientRpc(CatSoundType sound)
    {
        AudioClip clip = sound switch
        {
            CatSoundType.Attack => attackSound,
            CatSoundType.Jump   => jumpSound,
            CatSoundType.Stun   => stunSound,
            _ => null
        };

        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
