using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityAI.ResaleSystem.PriceEngine
{
    /// <summary>
    /// 价格引擎状态管理
    /// </summary>
    [Serializable]
    public class PriceEngineState
    {
        public long GlobalSeed;                  // 全局随机种子
        public int CurrentHour;                  // 当前游戏时间（小时）
        public int CurrentDay;                   // 当前游戏时间（天）
        
        // 价格缓存（避免重复计算）
        [SerializeField]
        private Dictionary<string, float> _priceCache;
        
        // 暴涨窗口缓存
        [SerializeField]
        private Dictionary<string, SurgeWindow> _surgeCache;
        
        public PriceEngineState()
        {
            _priceCache = new Dictionary<string, float>();
            _surgeCache = new Dictionary<string, SurgeWindow>();
        }
        
        /// <summary>
        /// 获取缓存的价格
        /// </summary>
        public float GetCachedPrice(string key)
        {
            if (_priceCache.TryGetValue(key, out float price))
            {
                return price;
            }
            return -1f;
        }
        
        /// <summary>
        /// 设置缓存的价格
        /// </summary>
        public void SetCachedPrice(string key, float price)
        {
            _priceCache[key] = price;
        }
        
        /// <summary>
        /// 清除价格缓存
        /// </summary>
        public void ClearPriceCache()
        {
            _priceCache.Clear();
        }
        
        /// <summary>
        /// 获取暴涨窗口
        /// </summary>
        public SurgeWindow GetSurgeWindow(string districtId)
        {
            if (_surgeCache.TryGetValue(districtId, out SurgeWindow window))
            {
                return window;
            }
            return null;
        }
        
        /// <summary>
        /// 设置暴涨窗口
        /// </summary>
        public void SetSurgeWindow(string districtId, SurgeWindow window)
        {
            _surgeCache[districtId] = window;
        }
        
        /// <summary>
        /// 移除暴涨窗口
        /// </summary>
        public void RemoveSurgeWindow(string districtId)
        {
            _surgeCache.Remove(districtId);
        }
        
        /// <summary>
        /// 清除所有暴涨窗口
        /// </summary>
        public void ClearSurgeCache()
        {
            _surgeCache.Clear();
        }
        
        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public void ClearAllCache()
        {
            ClearPriceCache();
            ClearSurgeCache();
        }
    }
}

