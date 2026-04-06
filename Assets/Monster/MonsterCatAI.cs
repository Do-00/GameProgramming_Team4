using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class MonsterCatAI : NetworkBehaviour
{
    // 고양이의 상태를 정의합니다. (서버가 관리)
    public enum CatState
    {
        Idle,       // 플레이어 탐색 중
        Staring,    // 플레이어 발견, 응시 중
        Charging,   // 와다다 돌진 중
        Stunned     // 돌진 후 기절 상태
    }

    [Header("AI 설정")]
    [SerializeField] private float detectionRadius = 70f; // 플레이어 감지 범위
    [SerializeField] private LayerMask playerLayer;       // 플레이어 오브젝트 레이어
    [SerializeField] private float rotateSpeed = 10f;      // 플레이어를 향해 몸을 돌리는 속도

    [Header("행동 설정")]
    [SerializeField] private float stareDuration = 2f;    // 응시 시간

    [SerializeField] private float pounceInitialSpeed = 400f; // 처음에 튀어나가는 최대 속도
    [SerializeField] private float pounceFriction = 3f;      // 브레이크(마찰력)

    [SerializeField] private float stunDuration = 5f;     // 기절 시간

    // 클라이언트들이 애니메이션을 맞출 수 있도록 상태를 동기화
    private NetworkVariable<CatState> currentState = new NetworkVariable<CatState>(CatState.Idle);

    private Rigidbody rb;
    private Transform targetPlayer; // 락온된 플레이어
    private Vector3 chargeDirection; // 돌진할 고정된 방향
    private float stateTimer = 0f;    // 각 상태의 시간을 재는 타이머

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // 호스트가 소환될 때만 AI 루틴을 시작
    public override void OnNetworkSpawn()
    {
        if (!IsServer) return; // 클라이언트는 AI 계산 안 함

        // 클라이언트들이 애니메이션을 적용할 수 있도록 상태 변화 이벤트 연결
        currentState.OnValueChanged += OnCatStateChanged;

        StartCoroutine(AILoop());
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            currentState.OnValueChanged -= OnCatStateChanged;
        }
    }

    private IEnumerator AILoop()
    {
        // 서버가 켜져있는 동안 무한 반복
        while (true)
        {
            switch (currentState.Value)
            {
                case CatState.Idle:
                    yield return StartCoroutine(HandleIdleState());
                    break;
                case CatState.Staring:
                    yield return StartCoroutine(HandleStaringState());
                    break;
                case CatState.Charging:
                    yield return StartCoroutine(HandleChargingState());
                    break;
                case CatState.Stunned:
                    yield return StartCoroutine(HandleStunnedState());
                    break;
            }
        }
    }

    private IEnumerator HandleIdleState()
    {
        // 돌진 관성 제거 및 물리 멈춤
        rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);

        while (currentState.Value == CatState.Idle)
        {
            // 주변 플레이어 탐색 (가장 가까운 플레이어 선택)
            Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius, playerLayer);
            if (hits.Length > 0)
            {
                // 첫 번째 발견된 플레이어를 타겟으로 삼고 응시 상태로 전환
                targetPlayer = hits[0].transform;
                currentState.Value = CatState.Staring;
                yield break;
            }
            yield return new WaitForSeconds(0.5f); // 0.5초마다 탐색 (최적화 위해)
        }
    }

    private IEnumerator HandleStaringState()
    {
        // 멈춰서 응시
        rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
        stateTimer = stareDuration;

        while (stateTimer > 0)
        {
            stateTimer -= Time.deltaTime;

            if (targetPlayer != null)
            {
                // 플레이어를 향해 서서히 몸을 돌림
                Vector3 dir = (targetPlayer.position - transform.position).normalized;
                dir.y = 0; // 위아래로는 돌지 않음
                if (dir != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotateSpeed);
                }
            }
            yield return null;
        }

        // 돌진 방향 확정 및 상태 전환
        if (targetPlayer != null)
        {
            chargeDirection = transform.forward; // 바라보던 방향으로 확정
            currentState.Value = CatState.Charging;
        }
        else
        {
            // 타겟이 사라졌다면 아이들로 복귀
            currentState.Value = CatState.Idle;
        }
    }

    private IEnumerator HandleChargingState()
    {
        // 시작할 때 속도를 최고 속도로 맞춤
        float currentPounceSpeed = pounceInitialSpeed;

        // 시간을 재지 않고, 속도가 멈추면 루프를 탈출함
        while (currentPounceSpeed > 0.1f)
        {
            currentPounceSpeed = Mathf.Lerp(currentPounceSpeed, 0f, Time.deltaTime * pounceFriction);

            rb.linearVelocity = new Vector3(chargeDirection.x * currentPounceSpeed, rb.linearVelocity.y, chargeDirection.z * currentPounceSpeed);

            yield return null;
        }

        // 기절 상태로 전환
        currentState.Value = CatState.Stunned;
    }

    private IEnumerator HandleStunnedState()
    {
        // 브레이크, 관성 제거
        rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
        stateTimer = stunDuration;

        // 기절 시간 동안 대기
        yield return new WaitForSeconds(stunDuration);

        // 기절 끝, 다시 아이들로 복귀
        targetPlayer = null;
        currentState.Value = CatState.Idle;
    }

    // 상태 변화에 따른 애니메이션 처리
    private void OnCatStateChanged(CatState oldState, CatState newState)
    {
        switch (newState)
        {
            case CatState.Idle:
                break;
            case CatState.Staring:
                break;
            case CatState.Charging:
                break;
            case CatState.Stunned:
                break;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}