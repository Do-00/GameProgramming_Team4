using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class MonsterSpiderAI : NetworkBehaviour
{
    public enum SpiderState { Patrol, Chase }

    [Header("AI 설정")]
    [SerializeField] private float detectionRadius = 15f;
    [SerializeField] private float chaseRadius = 25f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private float temporaryChaseDuration = 7f;

    [Header("이동 설정")]
    [SerializeField] private float patrolSpeed = 5f;
    [SerializeField] private float chaseSpeed = 15f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("거미줄 설치 알고리즘")]
    [SerializeField] private GameObject webPrefab;
    [SerializeField] private float webDropInterval = 5f;
    [SerializeField] private float minWebDistance = 10f;
    [SerializeField] private int maxWebs = 10;
    [SerializeField] private LayerMask wallLayer;
    [SerializeField] private float maxWebLength = 15f;
    [SerializeField] private float webMinHeight = 1f;
    [SerializeField] private float webMaxHeight = 4f;

    private NetworkVariable<SpiderState> currentState = new NetworkVariable<SpiderState>(SpiderState.Patrol);

    private Rigidbody rb;
    private Transform targetPlayer;
    private float webTimer = 0f;
    private Vector3 currentPatrolDir;
    private float patrolChangeTimer = 0f;

    private bool isProximityChase = false;
    private float chaseTimer = 0f;

    private List<NetworkObject> activeWebsList = new List<NetworkObject>();

    private void Awake() { rb = GetComponent<Rigidbody>(); }

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
                currentState.Value = SpiderState.Chase;
                yield break;
            }

            patrolChangeTimer -= Time.deltaTime;
            if (patrolChangeTimer <= 0f) PickNewPatrolDirection();

            MoveTowards(currentPatrolDir, patrolSpeed);

            webTimer += Time.deltaTime;
            if (webTimer >= webDropInterval)
            {
                if (TryDropWeb())
                {
                    webTimer = 0f;
                }
                else
                {
                    webTimer = webDropInterval - 0.5f;
                }
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
    }
}