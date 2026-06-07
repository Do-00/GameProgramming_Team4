using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class MonsterSpiderAI : NetworkBehaviour
{
    // ✨ 상태에 'Attack'이 추가되었습니다.
    public enum SpiderState { Patrol, Chase, Attack }

    [Header("AI 설정")]
    [SerializeField] private float detectionRadius = 15f;
    [SerializeField] private float chaseRadius = 25f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private float temporaryChaseDuration = 7f;

    [Header("이동 설정")]
    [SerializeField] private float patrolSpeed = 5f;
    [SerializeField] private float chaseSpeed = 15f;
    [SerializeField] private float rotationSpeed = 10f;

    // ✨ [새로 추가됨] 공격 설정 (SpiderAttack 스크립트에서 가져옴)
    [Header("공격 설정")]
    [SerializeField] private float attackRange = 2.5f;       // 공격 사거리
    [SerializeField] private float attackDamage = 30f;       // 공격 데미지
    [SerializeField] private float pauseDuration = 0.5f;     // 공격 전 멈칫하는 시간
    [SerializeField] private float attackCooldown = 2.0f;    // 공격 쿨타임

    [Header("거미줄 설치 알고리즘")]
    [SerializeField] private GameObject webPrefab;
    [SerializeField] private float webDropInterval = 5f;
    [SerializeField] private float minWebDistance = 10f;
    [SerializeField] private int maxWebs = 10;
    [SerializeField] private LayerMask wallLayer;
    [SerializeField] private float maxWebLength = 15f;
    [SerializeField] private float webMinHeight = 1f;
    [SerializeField] private float webMaxHeight = 4f;

    [Header("사운드")]
    public AudioClip walkAlert_s;   // 플레이어 감지 직전(추적 전환 시) 1회 재생
    public AudioClip attack_s;      // 공격 시도 시 재생
    private AudioSource audioSource;

    private NetworkVariable<SpiderState> currentState = new NetworkVariable<SpiderState>(SpiderState.Patrol);

    private Rigidbody rb;
    private Transform targetPlayer;
    private float webTimer = 0f;
    private Vector3 currentPatrolDir;
    private float patrolChangeTimer = 0f;

    private bool isProximityChase = false;
    private float chaseTimer = 0f;

    private List<NetworkObject> activeWebsList = new List<NetworkObject>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f;
            audioSource.maxDistance = 30f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        StartCoroutine(AILoop());
    }

    private IEnumerator AILoop()
    {
        while (true)
        {
            switch (currentState.Value)
            {
                case SpiderState.Patrol:
                    yield return StartCoroutine(HandlePatrolState());
                    break;
                case SpiderState.Chase:
                    yield return StartCoroutine(HandleChaseState());
                    break;
                case SpiderState.Attack:
                    yield return StartCoroutine(HandleAttackState());
                    break;
            }
        }
    }

    private IEnumerator HandlePatrolState()
    {
        PickNewPatrolDirection();

        while (currentState.Value == SpiderState.Patrol)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius, playerLayer);
            if (hits.Length > 0)
            {
                targetPlayer = hits[0].transform;
                isProximityChase = true;
                PlaySoundClientRpc("walkAlert");
                currentState.Value = SpiderState.Chase;
                yield break;
            }

            patrolChangeTimer -= Time.deltaTime;
            if (patrolChangeTimer <= 0f) PickNewPatrolDirection();

            MoveTowards(currentPatrolDir, patrolSpeed);

            webTimer += Time.deltaTime;
            if (webTimer >= webDropInterval)
            {
                if (TryDropWeb()) webTimer = 0f;
                else webTimer = webDropInterval - 0.5f;
            }

            yield return null;
        }
    }

    private IEnumerator HandleChaseState()
    {
        while (currentState.Value == SpiderState.Chase)
        {
            if (targetPlayer == null)
            {
                currentState.Value = SpiderState.Patrol;
                yield break;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.position);

            // ✨ [새로 추가됨] 거리가 사거리 안으로 들어오면 추적을 멈추고 공격 상태로 전환
            if (distanceToPlayer <= attackRange)
            {
                currentState.Value = SpiderState.Attack;
                yield break;
            }

            if (distanceToPlayer <= detectionRadius)
            {
                isProximityChase = true;
            }

            if (isProximityChase)
            {
                if (distanceToPlayer > chaseRadius)
                {
                    targetPlayer = null;
                    currentState.Value = SpiderState.Patrol;
                    yield break;
                }
            }
            else
            {
                chaseTimer -= Time.deltaTime;
                if (chaseTimer <= 0f)
                {
                    targetPlayer = null;
                    currentState.Value = SpiderState.Patrol;
                    yield break;
                }
            }

            Vector3 dir = (targetPlayer.position - transform.position).normalized;
            MoveTowards(dir, chaseSpeed);

            yield return null;
        }
    }

    private IEnumerator HandleAttackState()
    {
        // 1. 공격 시작 시 즉시 멈춤 (추적 로직이 아예 실행되지 않으므로 확실하게 멈춥니다)
        rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
        PlaySoundClientRpc("attack");

        // 2. 멈칫하는 시간 대기
        yield return new WaitForSeconds(pauseDuration);

        // 3. 멈칫한 후 데미지 판정
        if (targetPlayer != null)
        {
            float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.position);

            // 아직 거미 사거리 근처에 있다면 데미지 적용
            if (distanceToPlayer <= attackRange + 1f)
            {
                PlayerMovement player = targetPlayer.GetComponent<PlayerMovement>();
                if (player != null && player.currentHealth.Value > 0)
                {
                    player.TakeDamage(attackDamage);
                    Debug.Log("[서버 거미] 콱! 플레이어에게 데미지를 입혔습니다.");
                }
            }
        }

        // 4. 쿨타임 대기
        yield return new WaitForSeconds(attackCooldown);

        // 5. 공격이 끝나면 다시 추적(또는 정찰) 상태로 복귀
        currentState.Value = targetPlayer != null ? SpiderState.Chase : SpiderState.Patrol;
    }

    private void MoveTowards(Vector3 dir, float speed)
    {
        dir.y = 0;
        if (dir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);
            rb.linearVelocity = new Vector3(dir.x * speed, rb.linearVelocity.y, dir.z * speed);
        }
    }

    private void PickNewPatrolDirection()
    {
        float randomAngle = Random.Range(0f, 360f);
        currentPatrolDir = new Vector3(Mathf.Cos(randomAngle), 0, Mathf.Sin(randomAngle)).normalized;
        patrolChangeTimer = Random.Range(2f, 5f);
    }

    private bool TryDropWeb()
    {
        activeWebsList.RemoveAll(web => web == null || !web.IsSpawned);

        foreach (var web in activeWebsList)
        {
            if (Vector3.Distance(transform.position, web.transform.position) < minWebDistance) return false;
        }

        float randomHeight = Random.Range(webMinHeight, webMaxHeight);
        Vector3 rayOrigin = transform.position + Vector3.up * randomHeight;

        Vector3 leftDir = -transform.right;
        Vector3 rightDir = transform.right;

        Vector3 leftStart = rayOrigin + leftDir * 1.0f;
        Vector3 rightStart = rayOrigin + rightDir * 1.0f;

        if (Physics.Raycast(leftStart, leftDir, out RaycastHit leftHit, maxWebLength, wallLayer))
        {
            if (Physics.Raycast(rightStart, rightDir, out RaycastHit rightHit, maxWebLength, wallLayer))
            {
                float totalDistance = Vector3.Distance(leftHit.point, rightHit.point);
                if (totalDistance > 2f)
                {
                    DropWeb(leftHit.point, rightHit.point);
                    return true;
                }
            }
        }
        return false;
    }

    private void DropWeb(Vector3 startPos, Vector3 endPos)
    {
        if (webPrefab == null) return;

        GameObject webObj = Instantiate(webPrefab, Vector3.zero, Quaternion.identity);
        NetworkObject netObj = webObj.GetComponent<NetworkObject>();
        netObj.Spawn();

        SpiderWeb webScript = webObj.GetComponent<SpiderWeb>();
        if (webScript != null) webScript.InitializeLine(this, startPos, endPos);

        activeWebsList.Add(netObj);

        int limit = maxWebs <= 0 ? 10 : maxWebs;
        if (activeWebsList.Count > limit)
        {
            NetworkObject oldestWeb = activeWebsList[0];
            if (oldestWeb != null && oldestWeb.IsSpawned) oldestWeb.Despawn(true);
            activeWebsList.RemoveAt(0);
        }
    }

    [ClientRpc]
    private void PlaySoundClientRpc(string clipName)
    {
        AudioClip clip = clipName switch
        {
            "walkAlert" => walkAlert_s,
            "attack"    => attack_s,
            _ => null
        };
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    public void AlertByWeb(Transform player)
    {
        if (currentState.Value == SpiderState.Chase && isProximityChase) return;

        targetPlayer = player;
        currentState.Value = SpiderState.Chase;

        isProximityChase = false;
        chaseTimer = temporaryChaseDuration;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, chaseRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}