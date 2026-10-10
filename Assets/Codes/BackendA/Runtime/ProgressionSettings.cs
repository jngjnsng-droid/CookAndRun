using System;
using UnityEngine;

namespace CookAndRun.Progression
{
    // Resources/ProgressionSettings.asset가 없으면 문서의 기본 챕터 값으로 실행한다.
    [CreateAssetMenu(menuName = "CookAndRun/Progression Settings")]
    public sealed class ProgressionSettings : ScriptableObject
    {
        public int initialWallet = 0;
        public bool allowNegativeWallet = true;
        [Min(1)] public float chapterDuration = 180f;
        public int[] chapterTargets = { 20000, 30000, 45000, 60000, 80000 };
        [Range(0, 100)] public int finalAccuracyPercent = 80;
        public bool showBasicHud = true;
        // 실제 가격이 미정이므로 기본 목록은 비워 둔다. 가격이 정해지면 여기에 등록한다.
        public ShopItemDefinition[] shopItems = Array.Empty<ShopItemDefinition>();

        public ChapterRules[] BuildRules()
        {
            if (chapterTargets == null || chapterTargets.Length != 5)
                throw new InvalidOperationException("챕터 목표 금액은 CH 1~5의 다섯 개가 필요합니다.");
            var rules = new ChapterRules[5];
            for (int i = 0; i < rules.Length; i++)
                rules[i] = new ChapterRules(i + 1, chapterDuration, chapterTargets[i], i == 4 ? finalAccuracyPercent : 0);
            return rules;
        }
    }

    [Serializable]
    public sealed class ShopItemDefinition
    {
        public string id;
        public string displayName;
        [TextArea] public string effectDescription;
        [Min(0)] public int price;
        [Min(1)] public int maxLevel = 1;
    }
}
