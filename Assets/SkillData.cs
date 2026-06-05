using UnityEngine;

/// <summary>
/// 스킬 하나의 데이터를 담는 ScriptableObject
///
/// [사용법]
/// Project 창 우클릭 → Create → SkillData → 스킬마다 하나씩 에셋 생성
/// 예) DashSkill.asset, SearchSkill.asset
///
/// [새 스킬 추가할 때]
/// 1. SkillType에 enum 값 하나 추가
/// 2. Create → SkillData로 에셋 생성
/// 3. Inspector에서 값 채우기
/// 4. SkillShopManager의 skillList에 드래그로 추가
/// → 코드 수정 없이 스킬 추가 가능
/// </summary>
[CreateAssetMenu(fileName = "NewSkill", menuName = "SkillData")]
public class SkillData : ScriptableObject
{
    [Header("기본 정보")]
    public SkillType skillType;       // 어떤 스킬인지 식별자
    public string skillName;          // 화면에 표시할 이름
    [TextArea] public string description; // 스킬 설명
    public Sprite icon;               // 상점 UI 아이콘 (없으면 비워도 됨)

    [Header("구매")]
    public int unlockCost;            // 해금에 필요한 알 개수

    [Header("강화 단계")]
    public UpgradeLevel[] upgrades;   // 강화 단계 배열 (없으면 강화 없는 스킬)
}

/// <summary>
/// 스킬 식별자 — 새 스킬 추가 시 여기에 값 추가
/// </summary>
public enum SkillType
{
    Dash,       // 대시 스킬 (속도 증가)
    Search,     // 탐색 스킬 (음식 투시)
    // 여기에 새 스킬 추가
    // Camouflage,
    // Decoy,
}

/// <summary>
/// 강화 단계 하나의 데이터
/// </summary>
[System.Serializable]
public class UpgradeLevel
{
    public string upgradeName;        // 강화 단계 이름 (예: "Lv.1", "지속시간 강화")
    [TextArea] public string upgradeDescription; // 강화 설명
    public int cost;                  // 이 단계 강화 비용 (알 개수)

    [Header("대시 스킬 강화값 (Dash 스킬만 사용)")]
    public float bonusDuration;       // 지속시간 추가 (초)

    [Header("탐색 스킬 강화값 (Search 스킬만 사용)")]
    public float bonusRadius;         // 탐색 범위 추가 (m)
}
