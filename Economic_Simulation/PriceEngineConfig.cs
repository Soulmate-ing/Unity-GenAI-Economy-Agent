using UnityEngine;

namespace CityAI.ResaleSystem.PriceEngine
{
    /// <summary>
    /// 价格引擎配置 - ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "PriceEngineConfig", menuName = "ResaleSystem/PriceEngineConfig")]
    public class PriceEngineConfig : ScriptableObject
    {
        [Header("波动参数")]
        [Tooltip("基础波动范围（0.2-0.5）")]
        [Range(0.2f, 0.5f)]
        public float BaseFluctRange = 0.3f;
        
        [Header("周期参数")]
        [Tooltip("价格波动周期（小时），默认72小时=3天")]
        public int WavePeriodHours = 72;
        
        [Header("暴涨参数")]
        [Tooltip("街区暴涨概率（0-1），默认5%")]
        [Range(0f, 1f)]
        public float SurgeProbability = 0.05f;
        
        [Tooltip("暴涨价格倍数（1.5-3.0）")]
        [Range(1.5f, 3.0f)]
        public float SurgeMultiplier = 2.0f;
        
        [Tooltip("暴涨最短持续时间（小时）")]
        public int SurgeMinHours = 1;
        
        [Tooltip("暴涨最长持续时间（小时）")]
        public int SurgeMaxHours = 48;
        
        [Header("价格比例")]
        [Tooltip("进货价/售卖价比例（0.7-0.95），保证可盈利")]
        [Range(0.7f, 0.95f)]
        public float BuySellRatio = 0.85f;
        
        [Tooltip("随机噪声强度（0-1）")]
        [Range(0f, 0.2f)]
        public float RandomNoiseStrength = 0.1f;
    }
}

