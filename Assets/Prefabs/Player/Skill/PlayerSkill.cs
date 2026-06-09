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

    [Header("SkillData 에셋 연결 (Inspector에서 드래그)")]
    [SerializeField] public SkillData dashSkillData;
    [SerializeField] public SkillData searchSkillData;

    [Header("UI — 쿨타임 슬라이더 (0=쿨중, 1=사용가능)")]
    [SerializeField] private Slider dashCooldownSlider;
    [SerializeField] private Slider searchCooldownSlider;

    [Header("UI — 스킬 슬롯 (배경/아이콘)")]
    [SerializeField] private Image dashSlotBg;
    [SerializeField] private GameObject dashSkillIcon;
    [SerializeField] private Image searchSlotBg;
    [SerializeField] private GameObject searchSkillIcon;
    [SerializeField] private Sprite skillLockedSprite;
    [SerializeField] private Sprite skillUnlockedSprite;

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
    public NetworkVariable<bool> dashUnlocked = new NetworkVariable<bool>(
    false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> searchUnlocked = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> dashUpgradeLevel = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> searchUpgradeLevel = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 내부 상태 (Owner 전용 — 서버 동기화 불필요)

    public float EffectiveDashDuration
    {
        get
        {
            float bonus = 0f;
            if (dashSkillData != null && dashSkillData.upgrades != null)
            {
                int cap = Mathf.Min(dashUpgradeLevel.Value, dashSkillData.upgrades.Length);
                for (int i = 0; i < cap; i++)
                    bonus += dashSkillData.upgrades[i].bonusDuration;
            }
            return dashSkillDuration + bonus;
        }
    }

    public float EffectiveSearchRadius
    {
        get
        {
            float bonus = 0f;
            if (searchSkillData != null && searchSkillData.upgrades != null)
            {
                int cap = Mathf.Min(searchUpgradeLevel.Value, searchSkillData.upgrades.Length);
                for (int i = 0; i < cap; i++)
                    bonus += searchSkillData.upgrades[i].bonusRadius;
            }
            return searchRadius + bonus;
        }
    }

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

    public override void OnNetworkSpawn()
    {
        if (IsOwner && SkillShopManager.Instance != null)
            SkillShopManager.Instance.RegisterLocalPlayer(this);

        if (IsOwner)
        {
            dashUnlocked.OnValueChanged += (_, _) => UpdateCooldownUI();
            searchUnlocked.OnValueChanged += (_, _) => UpdateCooldownUI();
        }
    }

    public override void OnNetworkDespawn()
    {
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
        float dashProgress = 1f - Mathf.Clamp01(dashCooldownRemaining / dashSkillCooldown);
        float searchProgress = 1f - Mathf.Clamp01(searchCooldownRemaining / searchCooldown);

        if (dashCooldownSlider != null)
            dashCooldownSlider.value = dashProgress;
        if (searchCooldownSlider != null)
            searchCooldownSlider.value = searchProgress;

        bool dashReady = dashUnlocked.Value && dashCooldownRemaining <= 0f;
        bool searchReady = searchUnlocked.Value && searchCooldownRemaining <= 0f;

        if (dashSlotBg != null && skillLockedSprite != null && skillUnlockedSprite != null)
            dashSlotBg.sprite = dashReady ? skillUnlockedSprite : skillLockedSprite;
        if (dashSkillIcon != null)
            dashSkillIcon.SetActive(dashUnlocked.Value);

        if (searchSlotBg != null && skillLockedSprite != null && skillUnlockedSprite != null)
            searchSlotBg.sprite = searchReady ? skillUnlockedSprite : skillLockedSprite;
        if (searchSkillIcon != null)
            searchSkillIcon.SetActive(searchUnlocked.Value);
    }

    // 입력 처리

    private void HandleSkillInput()
    {
        // 일시정지 중에는 입력 무시 (PlayerMovement와 동일한 조건)
        if (Cursor.lockState == CursorLockMode.None) return;

        // ── 대시 스킬 ──────────────────────
        if (Input.GetKeyDown(dashSkillKey) && dashUnlocked.Value && dashCooldownRemaining <= 0f)
        {
            dashCooldownRemaining = dashSkillCooldown;
            ActivateDashSkillServerRpc();
        }

        // ── 탐색 스킬 ──────────────────────
        if (Input.GetKeyDown(searchSkillKey) && searchUnlocked.Value && searchCooldownRemaining <= 0f)
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
        yield return new WaitForSeconds(EffectiveDashDuration);
        speedMultiplier.Value = 1f;
    }

    // 탐색 스킬 — 완전 클라이언트 로컬

    /// 반경 내 음식 오브젝트의 Outline을 켜서 벽 너머로도 보이게 함.
    /// [Outline 벽 투시 조건]
    /// Outline 컴포넌트(QuickOutline 등)의 머티리얼이
    /// ZTest Always로 설정되어 있어야 벽을 뚫고 보임.

    private void ActivateSearchSkill()
    {
        // 기존 탐색 결과 초기화
        ClearSearchOutlines();
        if (searchCoroutine != null) StopCoroutine(searchCoroutine);

        Collider[] hits = Physics.OverlapSphere(transform.position, EffectiveSearchRadius);

        foreach (Collider hit in hits)
        {
            if (!hit.CompareTag("Food")) continue;

            Outline outline = hit.GetComponent<Outline>();

            // 만약 음식에 Outline 컴포넌트가 없다면 무시합니다.
            if (outline == null)
            {
                Debug.LogWarning($"[탐색 스킬] {hit.name}에 Outline 컴포넌트가 없습니다!");
                continue;
            }

            outline.enabled = true;
            activeSearchOutlines.Add(outline);
        }

        Debug.Log($"[탐색 스킬] 반경 {EffectiveSearchRadius}m 내 음식 {activeSearchOutlines.Count}개 발견");

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

    [ServerRpc]
    public void PurchaseSkillServerRpc(SkillType skillType)
    {
        SkillData data = GetSkillData(skillType);
        if (data == null) return;

        if (IsUnlocked(skillType))
        {
            Debug.Log($"[상점] 이미 해금된 스킬: {skillType}");
            return;
        }

        if (GameManager.Instance.sharedEggCount.Value < data.unlockCost)
        {
            Debug.Log($"[상점] 알 부족 | 필요: {data.unlockCost} | 보유: {GameManager.Instance.sharedEggCount.Value}");
            return;
        }

        GameManager.Instance.sharedEggCount.Value -= data.unlockCost;
        SetUnlocked(skillType, true);
        Debug.Log($"[상점] {data.skillName} 구매 완료 | 남은 알: {GameManager.Instance.sharedEggCount.Value}");
    }

    [ServerRpc]
    public void UpgradeSkillServerRpc(SkillType skillType)
    {
        SkillData data = GetSkillData(skillType);
        if (data == null || data.upgrades == null || data.upgrades.Length == 0) return;

        if (!IsUnlocked(skillType))
        {
            Debug.Log($"[상점] 먼저 해금이 필요합니다: {skillType}");
            return;
        }

        int currentLevel = GetUpgradeLevel(skillType);

        if (currentLevel >= data.upgrades.Length)
        {
            Debug.Log($"[상점] 이미 최대 강화: {skillType}");
            return;
        }

        int cost = data.upgrades[currentLevel].cost;

        if (GameManager.Instance.sharedEggCount.Value < cost)
        {
            Debug.Log($"[상점] 알 부족 | 필요: {cost} | 보유: {GameManager.Instance.sharedEggCount.Value}");
            return;
        }

        GameManager.Instance.sharedEggCount.Value -= cost;
        SetUpgradeLevel(skillType, currentLevel + 1);
        Debug.Log($"[상점] {data.skillName} Lv.{currentLevel + 1} 강화 완료 | 남은 알: {GameManager.Instance.sharedEggCount.Value}");
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼 — 외부(SkillShopManager)에서도 사용
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public SkillData GetSkillData(SkillType type) => type switch
    {
        SkillType.Dash => dashSkillData,
        SkillType.Search => searchSkillData,
        _ => null
    };

    public bool IsUnlocked(SkillType type) => type switch
    {
        SkillType.Dash => dashUnlocked.Value,
        SkillType.Search => searchUnlocked.Value,
        _ => false
    };

    public int GetUpgradeLevel(SkillType type) => type switch
    {
        SkillType.Dash => dashUpgradeLevel.Value,
        SkillType.Search => searchUpgradeLevel.Value,
        _ => 0
    };

    private void SetUnlocked(SkillType type, bool value)
    {
        switch (type)
        {
            case SkillType.Dash: dashUnlocked.Value = value; break;
            case SkillType.Search: searchUnlocked.Value = value; break;
        }
    }

    private void SetUpgradeLevel(SkillType type, int level)
    {
        switch (type)
        {
            case SkillType.Dash: dashUpgradeLevel.Value = level; break;
            case SkillType.Search: searchUpgradeLevel.Value = level; break;
        }
    }
}
