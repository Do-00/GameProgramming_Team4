using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// 플레이어 스킬 시스템

public class PlayerSkill : NetworkBehaviour
{

    [Header("대시 스킬 설정")]
    [SerializeField] private KeyCode dashSkillKey = KeyCode.Q;
    [SerializeField] private float dashSpeedMultiplier = 3.5f; // 기본 이동속도의 몇 배
    [SerializeField] private float dashSkillDuration = 1.2f; // 효과 지속 시간(초)
    [SerializeField] private float dashSkillCooldown = 6f;   // 쿨타임(초)

    [Header("탐색 스킬 설정")]
    [SerializeField] private KeyCode searchSkillKey = KeyCode.G;
    [SerializeField] private float searchRadius = 25f;  // 탐색 범위(m)
    [SerializeField] private float searchDuration = 4f;   // 윤곽선 표시 지속 시간(초)
    [SerializeField] private float searchCooldown = 12f;  // 쿨타임(초)
    [SerializeField] private LayerMask foodLayer;            // 음식 레이어

    [Header("UI — 쿨타임 슬라이더 (0=쿨중, 1=사용가능)")]
    [SerializeField] private Slider dashCooldownSlider;
    [SerializeField] private Slider searchCooldownSlider;

    // 네트워크 변수 (서버 → 모든 클라이언트 동기화)

    /// <summary>
    /// PlayerMovement가 읽어서 최종 이동속도에 곱하는 배율.
    /// 평상시 1.0 / 대시 스킬 사용 중 dashSpeedMultiplier
    /// </summary>
    public NetworkVariable<float> speedMultiplier = new NetworkVariable<float>(
        1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // 내부 상태 (Owner 전용 — 서버 동기화 불필요)

    private float dashCooldownRemaining = 0f;
    private float searchCooldownRemaining = 0f;

    // 탐색 스킬로 켠 Outline 목록 (꺼줄 때 추적용)
    private readonly List<Outline> activeSearchOutlines = new List<Outline>();

    private Coroutine searchCoroutine;

    // Unity 이벤트

    private void Update()
    {
        // 내 캐릭터가 아니면 아무것도 하지 않음
        if (!IsOwner) return;

        TickCooldowns();
        UpdateCooldownUI();
        HandleSkillInput();
    }

    public override void OnNetworkDespawn()
    {
        // 탐색 아웃라인이 켜진 채로 오브젝트가 사라지는 상황 방지
        ClearSearchOutlines();
    }

    // 쿨타임 & UI

    private void TickCooldowns()
    {
        if (dashCooldownRemaining > 0f) dashCooldownRemaining -= Time.deltaTime;
        if (searchCooldownRemaining > 0f) searchCooldownRemaining -= Time.deltaTime;
    }

    private void UpdateCooldownUI()
    {
        // 슬라이더 값: 0 = 쿨다운 중, 1 = 사용 가능
        if (dashCooldownSlider != null)
            dashCooldownSlider.value = 1f - Mathf.Clamp01(dashCooldownRemaining / dashSkillCooldown);

        if (searchCooldownSlider != null)
            searchCooldownSlider.value = 1f - Mathf.Clamp01(searchCooldownRemaining / searchCooldown);
    }

    // 입력 처리

    private void HandleSkillInput()
    {
        // 일시정지 중에는 입력 무시 (PlayerMovement와 동일한 조건)
        if (Cursor.lockState == CursorLockMode.None) return;

        // ── 대시 스킬 ──────────────────────
        if (Input.GetKeyDown(dashSkillKey) && dashCooldownRemaining <= 0f)
        {
            dashCooldownRemaining = dashSkillCooldown;
            ActivateDashSkillServerRpc();
        }

        // ── 탐색 스킬 ──────────────────────
        if (Input.GetKeyDown(searchSkillKey) && searchCooldownRemaining <= 0f)
        {
            searchCooldownRemaining = searchCooldown;
            ActivateSearchSkill();
        }
    }

    // 대시 스킬 — 서버 권한

    /// 클라이언트가 스킬 발동을 서버에 요청.
    /// 서버가 speedMultiplier를 올렸다가 duration 후 복원.
    [ServerRpc]
    private void ActivateDashSkillServerRpc()
    {
        // 이미 대시 중이라면 코루틴 중복 방지
        StopCoroutine(nameof(DashSkillRoutine));
        StartCoroutine(DashSkillRoutine());
    }

    private IEnumerator DashSkillRoutine()
    {
        speedMultiplier.Value = dashSpeedMultiplier;
        yield return new WaitForSeconds(dashSkillDuration);
        speedMultiplier.Value = 1f;
    }

    // 탐색 스킬 — 완전 클라이언트 로컬

    /// 반경 내 음식 오브젝트의 Outline을 켜서 벽 너머로도 보이게 함.
    /// 시각 효과만이므로 서버 RPC 불필요.
    ///
    /// [Outline 벽 투시 조건]
    /// Outline 컴포넌트(QuickOutline 등)의 머티리얼이
    /// ZTest Always로 설정되어 있어야 벽을 뚫고 보임.
    
    private void ActivateSearchSkill()
    {
        // 기존 탐색 결과 초기화
        ClearSearchOutlines();
        if (searchCoroutine != null) StopCoroutine(searchCoroutine);

        // 반경 내 음식 탐색
        Collider[] hits = Physics.OverlapSphere(transform.position, searchRadius, foodLayer);
        foreach (Collider hit in hits)
        {
            Outline outline = hit.GetComponent<Outline>();
            if (outline == null) continue;

            outline.enabled = true;
            activeSearchOutlines.Add(outline);
        }

        Debug.Log($"[탐색 스킬] 반경 {searchRadius}m 내 음식 {activeSearchOutlines.Count}개 발견");

        searchCoroutine = StartCoroutine(SearchDurationRoutine());
    }

    private IEnumerator SearchDurationRoutine()
    {
        yield return new WaitForSeconds(searchDuration);
        ClearSearchOutlines();
    }

    private void ClearSearchOutlines()
    {
        foreach (Outline outline in activeSearchOutlines)
        {
            if (outline != null) outline.enabled = false;
        }
        activeSearchOutlines.Clear();
    }
}
