using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 스킬 상점 매니저
///
/// [씬 세팅 방법]
/// 1. 빈 GameObject 만들고 이 스크립트 붙이기
/// 2. Canvas 안에 shopPanel 만들기
/// 3. shopPanel 안에 아래 구조로 UI 구성:
///
///   shopPanel
///   ├─ EggCountText (TextMeshProUGUI) — 보유 알 표시
///   ├─ CloseButton (Button) — 닫기 버튼
///   └─ SkillListContent (빈 GameObject + Vertical Layout Group)
///       └─ [스킬 카드들이 여기에 동적으로 생성됨]
///
/// 4. SkillCardPrefab 만들기:
///   SkillCard (Panel)
///   ├─ SkillName (TextMeshProUGUI)
///   ├─ Description (TextMeshProUGUI)
///   ├─ Status (TextMeshProUGUI) — 해금 여부 / 강화 레벨 / 비용
///   ├─ BuyButton (Button)
///   └─ UpgradeButton (Button)
///
/// 5. Inspector에서 각 슬롯 연결 + skillDataList에 에셋 드래그
/// </summary>
public class SkillShopManager : MonoBehaviour
{
    public static SkillShopManager Instance;

    [Header("스킬 데이터 목록 (Inspector에서 에셋 드래그)")]
    public List<SkillData> skillDataList;

    [Header("UI 연결")]
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private Transform skillListContent;
    [SerializeField] private GameObject skillCardPrefab;
    [SerializeField] private TextMeshProUGUI eggCountText;
    [SerializeField] private Button closeButton;

    private bool isShopOpen = false;
    private PlayerSkill localPlayerSkill;

    // 초기화

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        shopPanel.SetActive(false);
        if (closeButton != null)
            closeButton.onClick.AddListener(CloseShop);
    }

    // 입력 감지

    private void Update()
    {
        // Tab 키
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (!isShopOpen)
            {
                // 쉘터 밖이면 무시
                if (!ShelterZone.IsLocalPlayerInShelter)
                {
                    Debug.Log("[상점] 쉘터 안에서만 열 수 있습니다.");
                    return;
                }
                OpenShop();
            }
            else
            {
                CloseShop();
            }
        }

        // 상점 열린 동안 알 개수 실시간 갱신
        if (isShopOpen && GameManager.Instance != null && eggCountText != null)
            eggCountText.text = $"보유 알: {GameManager.Instance.sharedEggCount.Value}";
    }

    // 상점 열기 / 닫기

    private void OpenShop()
    {
        isShopOpen = true;
        shopPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 내 PlayerSkill 캐싱 (처음 한 번만)
        if (localPlayerSkill == null)
            localPlayerSkill = FindLocalPlayerSkill();

        RefreshShopUI();
    }

    private void CloseShop()
    {
        isShopOpen = false;
        shopPanel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // UI 갱신

    private void RefreshShopUI()
    {
        // 기존 카드 전부 제거
        foreach (Transform child in skillListContent)
            Destroy(child.gameObject);

        if (localPlayerSkill == null)
        {
            Debug.LogWarning("[상점] 내 PlayerSkill을 찾지 못했습니다.");
            return;
        }

        // SkillData마다 카드 생성
        foreach (SkillData data in skillDataList)
            SetupSkillCard(data);
    }

    /// <summary>스킬 카드 하나 생성 및 버튼 세팅</summary>
    private void SetupSkillCard(SkillData data)
    {
        GameObject card = Instantiate(skillCardPrefab, skillListContent);
        Transform root = card.transform.Find("SkillCard") ?? card.transform;

        bool isUnlocked = localPlayerSkill.IsUnlocked(data.skillType);
        int currentLevel = localPlayerSkill.GetUpgradeLevel(data.skillType);
        bool isMaxLevel = data.upgrades == null || currentLevel >= data.upgrades.Length;

        // ── 텍스트 ──
        root.Find("SkillName").GetComponent<TextMeshProUGUI>().text = data.skillName;
        root.Find("Description").GetComponent<TextMeshProUGUI>().text = data.description;

        var statusText = root.Find("Status").GetComponent<TextMeshProUGUI>();
        if (!isUnlocked)
            statusText.text = $"미해금  |  구매 비용: {data.unlockCost} 알";
        else if (isMaxLevel)
            statusText.text = $"해금됨  |  최대 강화 (Lv.{currentLevel})";
        else
            statusText.text = $"해금됨  |  Lv.{currentLevel}  |  강화 비용: {data.upgrades[currentLevel].cost} 알";

        // ── 구매 버튼 (미해금일 때만 표시) ──
        var buyBtn = root.Find("BuyButton").GetComponent<Button>();
        buyBtn.gameObject.SetActive(!isUnlocked);
        buyBtn.onClick.AddListener(() =>
        {
            localPlayerSkill.PurchaseSkillServerRpc(data.skillType);
            Invoke(nameof(RefreshShopUI), 0.15f); // 서버 처리 후 UI 갱신
        });

        // ── 강화 버튼 (해금 + 최대 레벨 아닐 때만 표시) ──
        var upgradeBtn = root.Find("UpgradeButton").GetComponent<Button>();
        upgradeBtn.gameObject.SetActive(isUnlocked && !isMaxLevel);
        upgradeBtn.onClick.AddListener(() =>
        {
            localPlayerSkill.UpgradeSkillServerRpc(data.skillType);
            Invoke(nameof(RefreshShopUI), 0.15f);
        });
    }

    // 헬퍼

    public void RegisterLocalPlayer(PlayerSkill skill)
    {
        localPlayerSkill = skill;
        Debug.Log("[상점] 로컬 PlayerSkill 등록 완료");
    }

    private PlayerSkill FindLocalPlayerSkill()
    {
        foreach (var skill in FindObjectsByType<PlayerSkill>(FindObjectsSortMode.None))
            if (skill.IsOwner) return skill;
        return null;
    }
}