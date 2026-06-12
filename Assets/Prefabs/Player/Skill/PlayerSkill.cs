using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class PlayerSkill : NetworkBehaviour
{
    [Header("대시 스킬 설정")]
    [SerializeField] private KeyCode dashSkillKey = KeyCode.Q;
    [SerializeField] private float dashSpeedMultiplier = 3.5f;
    [SerializeField] private float dashSkillDuration = 1.2f;
    [SerializeField] private float dashSkillCooldown = 6f;

    [Header("탐색 스킬 설정")]
    [SerializeField] private KeyCode searchSkillKey = KeyCode.G;
    [SerializeField] private float searchRadius = 25f;
    [SerializeField] private float searchDuration = 4f;
    [SerializeField] private float searchCooldown = 12f;
    [SerializeField] private LayerMask foodLayer;

    [Header("스킬 데이터 에셋")]
    [SerializeField] public SkillData dashSkillData;
    [SerializeField] public SkillData searchSkillData;

    [Header("UI — 쿨타임 슬라이더 (0=쿨중, 1=사용가능)")]
    [SerializeField] private Slider dashCooldownSlider;
    [SerializeField] private Slider searchCooldownSlider;

    [Header("UI — 스킬 슬롯")]
    [SerializeField] private Image dashSlotBg;
    [SerializeField] private GameObject dashSkillIcon;
    [SerializeField] private Image searchSlotBg;
    [SerializeField] private GameObject searchSkillIcon;
    [SerializeField] private Sprite skillLockedSprite;
    [SerializeField] private Sprite skillUnlockedSprite;

    // PlayerMovement의 SubmitMovementServerRpc에서 읽어 최종 이동속도에 곱함
    // 평상시 1.0 / 대시 스킬 발동 중 dashSpeedMultiplier
    public NetworkVariable<float> speedMultiplier = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> dashUnlocked = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> searchUnlocked = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> dashUpgradeLevel = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> searchUpgradeLevel = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float dashCooldownRemaining = 0f;
    private float searchCooldownRemaining = 0f;
    private readonly List<Outline> activeSearchOutlines = new List<Outline>();
    private Coroutine searchCoroutine;

    // 현재 강화 레벨에 따라 bonusDuration을 누적 합산하여 실제 지속시간을 계산
    public float EffectiveDashDuration
    {
        get
        {
            float bonus = 0f;
            if (dashSkillData?.upgrades != null)
            {
                int cap = Mathf.Min(dashUpgradeLevel.Value, dashSkillData.upgrades.Length);
                for (int i = 0; i < cap; i++)
                    bonus += dashSkillData.upgrades[i].bonusDuration;
            }
            return dashSkillDuration + bonus;
        }
    }

    // 현재 강화 레벨에 따라 bonusRadius를 누적 합산하여 실제 탐색 반경을 계산
    public float EffectiveSearchRadius
    {
        get
        {
            float bonus = 0f;
            if (searchSkillData?.upgrades != null)
            {
                int cap = Mathf.Min(searchUpgradeLevel.Value, searchSkillData.upgrades.Length);
                for (int i = 0; i < cap; i++)
                    bonus += searchSkillData.upgrades[i].bonusRadius;
            }
            return searchRadius + bonus;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        SkillShopManager.Instance?.RegisterLocalPlayer(this);
        // 해금 여부가 변경되면 즉시 UI를 갱신하여 스킬 슬롯 상태를 반영
        dashUnlocked.OnValueChanged += (_, _) => UpdateCooldownUI();
        searchUnlocked.OnValueChanged += (_, _) => UpdateCooldownUI();
    }

    public override void OnNetworkDespawn()
    {
        ClearSearchOutlines();
    }

    private void Update()
    {
        if (!IsOwner) return;

        TickCooldowns();
        UpdateCooldownUI();
        HandleSkillInput();
    }

    private void TickCooldowns()
    {
        if (dashCooldownRemaining > 0f) dashCooldownRemaining -= Time.deltaTime;
        if (searchCooldownRemaining > 0f) searchCooldownRemaining -= Time.deltaTime;
    }

    // 슬라이더 값은 0=쿨중 / 1=사용가능 범위로 정규화
    // 해금 + 쿨타임 완료 상태일 때만 슬롯 배경 이미지를 사용가능 스프라이트로 교체
    private void UpdateCooldownUI()
    {
        float dashProgress = 1f - Mathf.Clamp01(dashCooldownRemaining / dashSkillCooldown);
        float searchProgress = 1f - Mathf.Clamp01(searchCooldownRemaining / searchCooldown);

        if (dashCooldownSlider != null) dashCooldownSlider.value = dashProgress;
        if (searchCooldownSlider != null) searchCooldownSlider.value = searchProgress;

        bool dashReady = dashUnlocked.Value && dashCooldownRemaining <= 0f;
        bool searchReady = searchUnlocked.Value && searchCooldownRemaining <= 0f;

        if (dashSlotBg != null && skillLockedSprite != null && skillUnlockedSprite != null)
            dashSlotBg.sprite = dashReady ? skillUnlockedSprite : skillLockedSprite;
        dashSkillIcon?.SetActive(dashUnlocked.Value);

        if (searchSlotBg != null && skillLockedSprite != null && skillUnlockedSprite != null)
            searchSlotBg.sprite = searchReady ? skillUnlockedSprite : skillLockedSprite;
        searchSkillIcon?.SetActive(searchUnlocked.Value);
    }

    private void HandleSkillInput()
    {
        if (Cursor.lockState == CursorLockMode.None) return;

        if (Input.GetKeyDown(dashSkillKey) && dashUnlocked.Value && dashCooldownRemaining <= 0f)
        {
            // 쿨타임은 로컬에서 바로 시작하고, 실제 효과는 서버에 요청
            dashCooldownRemaining = dashSkillCooldown;
            ActivateDashSkillServerRpc();
        }

        if (Input.GetKeyDown(searchSkillKey) && searchUnlocked.Value && searchCooldownRemaining <= 0f)
        {
            searchCooldownRemaining = searchCooldown;
            ActivateSearchSkill();
        }
    }

    [ServerRpc]
    private void ActivateDashSkillServerRpc()
    {
        // 이전 대시가 남아있을 경우 중복 코루틴 방지를 위해 먼저 중단
        StopCoroutine(nameof(DashSkillRoutine));
        StartCoroutine(DashSkillRoutine());
    }

    private IEnumerator DashSkillRoutine()
    {
        speedMultiplier.Value = dashSpeedMultiplier;
        yield return new WaitForSeconds(EffectiveDashDuration);
        speedMultiplier.Value = 1f;
    }

    // 탐색 스킬은 아웃라인 시각 효과만 처리하므로 서버 동기화 없이 로컬에서만 실행
    // OverlapSphere로 범위 안의 음식을 모두 찾아 Outline 컴포넌트를 켜고 searchDuration 후 끔
    private void ActivateSearchSkill()
    {
        ClearSearchOutlines();
        if (searchCoroutine != null) StopCoroutine(searchCoroutine);

        foreach (Collider hit in Physics.OverlapSphere(transform.position, EffectiveSearchRadius))
        {
            if (!hit.CompareTag("Food")) continue;

            Outline outline = hit.GetComponent<Outline>();
            if (outline == null)
            {
                Debug.LogWarning($"[탐색 스킬] {hit.name}에 Outline 컴포넌트가 없습니다.");
                continue;
            }

            outline.enabled = true;
            activeSearchOutlines.Add(outline);
        }

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

    // 스킬 구매: 공유 알 재화를 소모하고 해당 스킬을 해금
    [ServerRpc]
    public void PurchaseSkillServerRpc(SkillType skillType)
    {
        SkillData data = GetSkillData(skillType);
        if (data == null || IsUnlocked(skillType)) return;

        if (GameManager.Instance.sharedEggCount.Value < data.unlockCost)
        {
            Debug.Log($"[상점] 알 부족. 필요: {data.unlockCost} / 보유: {GameManager.Instance.sharedEggCount.Value}");
            return;
        }

        GameManager.Instance.sharedEggCount.Value -= data.unlockCost;
        SetUnlocked(skillType, true);
        Debug.Log($"[상점] {data.skillName} 구매 완료. 남은 알: {GameManager.Instance.sharedEggCount.Value}");
    }

    // 스킬 강화: 현재 레벨의 비용을 소모하고 업그레이드 레벨을 1 증가
    // upgrades 배열의 인덱스가 곧 강화 레벨이므로 currentLevel을 그대로 인덱스로 사용
    [ServerRpc]
    public void UpgradeSkillServerRpc(SkillType skillType)
    {
        SkillData data = GetSkillData(skillType);
        if (data?.upgrades == null || data.upgrades.Length == 0) return;
        if (!IsUnlocked(skillType)) return;

        int currentLevel = GetUpgradeLevel(skillType);
        if (currentLevel >= data.upgrades.Length) return;

        int cost = data.upgrades[currentLevel].cost;
        if (GameManager.Instance.sharedEggCount.Value < cost)
        {
            Debug.Log($"[상점] 알 부족. 필요: {cost} / 보유: {GameManager.Instance.sharedEggCount.Value}");
            return;
        }

        GameManager.Instance.sharedEggCount.Value -= cost;
        SetUpgradeLevel(skillType, currentLevel + 1);
        Debug.Log($"[상점] {data.skillName} Lv.{currentLevel + 1} 강화 완료.");
    }

    public SkillData GetSkillData(SkillType type) => type switch
    {
        SkillType.Dash   => dashSkillData,
        SkillType.Search => searchSkillData,
        _ => null
    };

    public bool IsUnlocked(SkillType type) => type switch
    {
        SkillType.Dash   => dashUnlocked.Value,
        SkillType.Search => searchUnlocked.Value,
        _ => false
    };

    public int GetUpgradeLevel(SkillType type) => type switch
    {
        SkillType.Dash   => dashUpgradeLevel.Value,
        SkillType.Search => searchUpgradeLevel.Value,
        _ => 0
    };

    private void SetUnlocked(SkillType type, bool value)
    {
        switch (type)
        {
            case SkillType.Dash:   dashUnlocked.Value = value;   break;
            case SkillType.Search: searchUnlocked.Value = value; break;
        }
    }

    private void SetUpgradeLevel(SkillType type, int level)
    {
        switch (type)
        {
            case SkillType.Dash:   dashUpgradeLevel.Value = level;   break;
            case SkillType.Search: searchUpgradeLevel.Value = level; break;
        }
    }
}
