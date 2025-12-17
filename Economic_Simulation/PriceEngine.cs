using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CityAI.ResaleSystem.Model;
using CityAI.Common.Events;
using CityAI.Common;
using CityAI.Common.Debug;
using CityAI.ResaleSystem.Events;

namespace CityAI.ResaleSystem.PriceEngine
{
    /// <summary>
    /// 价格引擎核心类
    /// 负责商品价格计算和暴涨机制管理
    /// </summary>
    public class PriceEngine : MonoBehaviour
    {
        private long _globalSeed;
        private PriceEngineConfig _config;
        private PriceEngineState _state;
        private Dictionary<string, SurgeWindow> _activeSurges;
        
        // 事件
        public event Action<string, string, float> OnSurgeStarted;
        public event Action<string> OnSurgeEnded;
        
        private void Awake()
        {
            _state = new PriceEngineState();
            _activeSurges = new Dictionary<string, SurgeWindow>();
        }
        
        /// <summary>
        /// 初始化价格引擎
        /// </summary>
        public void Initialize(long globalSeed, PriceEngineConfig config)
        {
            // 确保字典已初始化（防御性编程）
            if (_activeSurges == null)
            {
                _activeSurges = new Dictionary<string, SurgeWindow>();
            }
            if (_state == null)
            {
                _state = new PriceEngineState();
            }
            
            // 清除之前的暴涨状态（避免状态残留）
            _activeSurges.Clear();
            _state.ClearSurgeCache();
            
            _globalSeed = globalSeed;
            _config = config;
            _state.GlobalSeed = globalSeed;
            
            // 取消之前的订阅（避免重复订阅）
            if (GameTimeManager.Instance != null)
            {
                GameTimeManager.Instance.OnHourAdvanced -= OnGameTimeAdvanced;
                GameTimeManager.Instance.OnDayChanged -= OnDayChanged;
            }
            
            // 订阅GameTimeManager事件
            if (GameTimeManager.Instance != null)
            {
                GameTimeManager.Instance.OnHourAdvanced += OnGameTimeAdvanced;
                GameTimeManager.Instance.OnDayChanged += OnDayChanged;
            }
            
            Debug.Log($"[PriceEngine] 初始化完成，种子: {globalSeed}");
            
            // 注册调试数据
            RegisterDebugData();
        }
        
        /// <summary>
        /// 注册调试数据到DebugDataService
        /// </summary>
        private void RegisterDebugData()
        {
            DebugDataService.Register(
                "PriceEngine.GlobalSeed",
                () => _globalSeed.ToString(),
                "PriceEngine",
                0
            );
            
            DebugDataService.Register(
                "PriceEngine.ActiveSurges",
                () => _activeSurges != null ? _activeSurges.Count.ToString() : "0",
                "PriceEngine",
                1
            );
            
            DebugDataService.Register(
                "PriceEngine.ConfigLoaded",
                () => _config != null ? "是" : "否",
                "PriceEngine",
                2
            );
        }
        
        private void OnDestroy()
        {
            // 取消订阅
            if (GameTimeManager.Instance != null)
            {
                GameTimeManager.Instance.OnHourAdvanced -= OnGameTimeAdvanced;
                GameTimeManager.Instance.OnDayChanged -= OnDayChanged;
            }
        }
        
        /// <summary>
        /// 每小时更新
        /// </summary>
        private void OnGameTimeAdvanced(int totalHours)
        {
            int day = totalHours / 24 + 1; // 加1因为第1天从总小时数0开始
            UpdateTime(totalHours, day);
        }
        
        /// <summary>
        /// 每天更新
        /// </summary>
        private void OnDayChanged(int day)
        {
            _state.CurrentDay = day;
            
            // 每3天检查暴涨
            if (day % 3 == 0)
            {
                CheckAllDistricts();
            }
        }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        public void UpdateTime(int hour, int day)
        {
            int previousDay = _state.CurrentDay;
            int previousHour = _state.CurrentHour;
            
            // ⭐ 检测时间跳跃（大跳跃 = 超过1天）
            bool isTimeJump = false;
            int dayDiff = day - previousDay;
            int hourDiff = hour - previousHour;
            
            // 如果天数差异 > 1，或者小时数差异很大（可能是时间跳跃），标记为时间跳跃
            if (dayDiff > 1 || (dayDiff == 1 && hourDiff > 24))
            {
                isTimeJump = true;
                Debug.Log($"[PriceEngine] 检测到时间跳跃：从 Day {previousDay} Hour {previousHour} 跳跃到 Day {day} Hour {hour} (差异: {dayDiff}天)");
            }
            
            _state.CurrentHour = hour;
            _state.CurrentDay = day;
            
            // 清除当天缓存
            _state.ClearPriceCache();
            
            // ⭐ 时间跳跃时，强制检查所有街区（忽略3天限制）
            if (isTimeJump)
            {
                Debug.Log("[PriceEngine] 因时间跳跃，强制检查所有街区热区");
                // 先清理已过期的热区
                CheckActiveSurges();
                // 然后强制检查所有街区
                var allDistricts = DistrictCatalog.GetAllDistricts();
                foreach (var district in allDistricts)
                {
                    ForceCheckSurgeForDistrict(district.Id);
                }
                Debug.Log($"[PriceEngine] 时间跳跃后，活跃热区数量: {GetActiveSurges().Count}");
            }
            // 如果进入新的一天，且是3的倍数，正常检查暴涨
            else if (day != previousDay && day % 3 == 0)
            {
                CheckAllDistricts();
            }
            
            // 检查是否还有活跃的暴涨窗口
            CheckActiveSurges();
        }
        
        /// <summary>
        /// 获取进货价
        /// </summary>
        public float GetBuyPrice(string goodsId, int hour)
        {
            if (_config == null)
            {
                Debug.LogError("[PriceEngine] 未初始化配置");
                return 0f;
            }
            
            // 检查缓存
            string cacheKey = $"BUY_{goodsId}_{hour}";
            float cachedPrice = _state.GetCachedPrice(cacheKey);
            if (cachedPrice >= 0f)
            {
                return cachedPrice;
            }
            
            // 生成基础价格
            float basePrice = GenerateBasePrice(goodsId, hour);
            
            // 进货价 = 基础价格 * BuySellRatio
            float buyPrice = basePrice * _config.BuySellRatio;
            
            // 确保价格不会太低
            Product product = ProductCatalog.GetProduct(goodsId);
            if (product != null)
            {
                buyPrice = Mathf.Max(buyPrice, product.BasePrice * 0.5f);
            }
            
            // 缓存价格
            _state.SetCachedPrice(cacheKey, buyPrice);
            
            return buyPrice;
        }
        
        /// <summary>
        /// 获取售卖价（考虑暴涨）
        /// </summary>
        public float GetSellPrice(string goodsId, string districtId, int hour)
        {
            if (_config == null)
            {
                Debug.LogError("[PriceEngine] 未初始化配置");
                return 0f;
            }
            
            // 检查缓存
            string cacheKey = $"SELL_{goodsId}_{districtId}_{hour}";
            float cachedPrice = _state.GetCachedPrice(cacheKey);
            if (cachedPrice >= 0f)
            {
                return cachedPrice;
            }
            
            // 生成基础价格
            float sellPrice = GenerateBasePrice(goodsId, hour);
            
            // 检查是否在暴涨状态
            if (IsSurging(districtId, hour, goodsId))
            {
                sellPrice *= _config.SurgeMultiplier;
            }
            
            // 确保价格不会太低
            Product product = ProductCatalog.GetProduct(goodsId);
            if (product != null)
            {
                sellPrice = Mathf.Max(sellPrice, product.BasePrice * 0.5f);
            }
            
            // 缓存价格
            _state.SetCachedPrice(cacheKey, sellPrice);
            
            return sellPrice;
        }
        
        /// <summary>
        /// 生成基础价格
        /// </summary>
        private float GenerateBasePrice(string goodsId, int hour)
        {
            Product product = ProductCatalog.GetProduct(goodsId);
            if (product == null)
            {
                Debug.LogWarning($"[PriceEngine] 未找到商品: {goodsId}");
                return 0f;
            }
            
            float basePrice = product.BasePrice;
            
            // 计算波动部分（正弦波）
            int hourIndex = hour % _config.WavePeriodHours;
            float waveValue = Mathf.Sin(2f * Mathf.PI * hourIndex / _config.WavePeriodHours) 
                            * _config.BaseFluctRange;
            
            // 添加随机噪声
            System.Random rng = new System.Random((int)(_globalSeed + goodsId.GetHashCode() + hour));
            float randomNoise = ((float)rng.NextDouble() * 2f - 1f) * _config.RandomNoiseStrength;
            
            // 计算最终价格
            float finalPrice = basePrice * (1f + waveValue) * (1f + randomNoise);
            
            return finalPrice;
        }
        
        /// <summary>
        /// 街区是否正在暴涨
        /// </summary>
        public bool IsSurging(string districtId, int hour)
        {
            if (_activeSurges.TryGetValue(districtId, out SurgeWindow window))
            {
                return window.IsInWindow(hour);
            }
            return false;
        }
        
        /// <summary>
        /// 街区是否正在暴涨特定商品
        /// </summary>
        public bool IsSurging(string districtId, int hour, string goodsId)
        {
            if (_activeSurges.TryGetValue(districtId, out SurgeWindow window))
            {
                return window.IsInWindow(hour) && window.GoodsId == goodsId;
            }
            return false;
        }
        
        /// <summary>
        /// 获取暴涨商品ID
        /// </summary>
        public string GetSurgingGoodsId(string districtId)
        {
            if (_activeSurges.TryGetValue(districtId, out SurgeWindow window) && window.IsActive)
            {
                return window.GoodsId;
            }
            return null;
        }
        
        /// <summary>
        /// 检查所有街区的暴涨状态
        /// </summary>
        private void CheckAllDistricts()
        {
            var allDistricts = DistrictCatalog.GetAllDistricts();
            foreach (var district in allDistricts)
            {
                CheckSurgeForDistrict(district.Id, _state.CurrentDay);
            }
        }
        
        /// <summary>
        /// 检查并触发暴涨
        /// </summary>
        private void CheckSurgeForDistrict(string districtId, int day)
        {
            // 每3天检测一次
            if (day % 3 != 0) return;
            
            // 检查是否已存在暴涨窗口（如果已存在且还未结束，不重复创建）
            if (_activeSurges.TryGetValue(districtId, out SurgeWindow existing))
            {
                // 如果窗口还在有效期内，不创建新的
                if (_state.CurrentHour <= existing.EndHour)
                {
                    return;
                }
                // 如果窗口已过期，先移除
                _activeSurges.Remove(districtId);
                _state.RemoveSurgeWindow(districtId);
            }
            
            // 确保ProductCatalog已初始化
            if (ProductCatalog.GetProductCount() == 0)
            {
                ProductCatalog.Initialize();
            }
            
            // 概率触发
            System.Random rng = new System.Random((int)(_globalSeed + districtId.GetHashCode() + day));
            float randomValue = (float)rng.NextDouble();
            
            if (randomValue < _config.SurgeProbability)
            {
                // 选择随机商品
                var allProducts = ProductCatalog.GetAllProducts();
                if (allProducts.Count == 0) return;
                
                int randomIndex = rng.Next(allProducts.Count);
                Product surgeProduct = allProducts[randomIndex];
                
                // 确定暴涨时长
                int duration = rng.Next(_config.SurgeMinHours, _config.SurgeMaxHours + 1);
                
                // 创建暴涨窗口
                SurgeWindow window = new SurgeWindow
                {
                    DistrictId = districtId,
                    GoodsId = surgeProduct.Id,
                    StartHour = _state.CurrentHour,
                    EndHour = _state.CurrentHour + duration - 1,
                    IsActive = true
                };
                
                _activeSurges[districtId] = window;
                _state.SetSurgeWindow(districtId, window);
                
                Debug.Log($"[PriceEngine] 街区 {districtId} 爆发 {surgeProduct.Name} 暴涨！持续 {duration} 小时");
                
                // 触发事件
                OnSurgeStarted?.Invoke(districtId, surgeProduct.Id, _config.SurgeMultiplier);
                
                // 发布EventBus事件
                if (EventBus.Instance != null)
                {
                    EventBus.Instance.Publish(new SurgeStartedEvent
                    {
                        DistrictId = districtId,
                        ProductId = surgeProduct.Id,
                        SurgeMultiplier = _config.SurgeMultiplier,
                        Source = "PriceEngine"
                    });
                }
            }
        }
        
        /// <summary>
        /// 检查活跃的暴涨窗口
        /// </summary>
        private void CheckActiveSurges()
        {
            List<string> endedDistricts = new List<string>();
            
            foreach (var kvp in _activeSurges)
            {
                SurgeWindow window = kvp.Value;
                
                // 检查暴涨是否结束
                if (_state.CurrentHour > window.EndHour)
                {
                    endedDistricts.Add(kvp.Key);
                }
            }
            
            // 清理已结束的暴涨
            foreach (string districtId in endedDistricts)
            {
                _activeSurges.Remove(districtId);
                _state.RemoveSurgeWindow(districtId);
                
                Debug.Log($"[PriceEngine] 街区 {districtId} 暴涨结束");
                
                // 触发事件
                OnSurgeEnded?.Invoke(districtId);
                
                // 发布EventBus事件
                if (EventBus.Instance != null)
                {
                    EventBus.Instance.Publish(new SurgeEndedEvent
                    {
                        DistrictId = districtId,
                        Source = "PriceEngine"
                    });
                }
            }
        }
        
        /// <summary>
        /// 获取当前所有活跃的暴涨窗口（供AI系统调用）
        /// </summary>
        public List<SurgeWindow> GetActiveSurges()
        {
            List<SurgeWindow> activeSurges = new List<SurgeWindow>();
            if (_activeSurges == null) return activeSurges;
            
            foreach (var kvp in _activeSurges)
            {
                SurgeWindow window = kvp.Value;
                if (window.IsInWindow(_state.CurrentHour))
                {
                    activeSurges.Add(window);
                }
            }
            return activeSurges;
        }
        
        /// <summary>
        /// 获取当前游戏时间（小时）
        /// </summary>
        public int GetCurrentHour()
        {
            return _state != null ? _state.CurrentHour : 0;
        }
        
        /// <summary>
        /// 获取当前游戏日期
        /// </summary>
        public int GetCurrentDay()
        {
            return _state != null ? _state.CurrentDay : 1;
        }
        
        /// <summary>
        /// 强制检查所有街区的热区（测试用，忽略天数限制）
        /// </summary>
        public void ForceCheckAllSurges()
        {
            if (_config == null)
            {
                Debug.LogError("[PriceEngine] 未初始化，无法强制检查热区");
                return;
            }
            
            Debug.Log($"[PriceEngine] 强制检查热区 - 当前状态: Day {_state.CurrentDay}, Hour {_state.CurrentHour}");
            
            // 强制检查所有街区（忽略天数限制）
            var allDistricts = DistrictCatalog.GetAllDistricts();
            foreach (var district in allDistricts)
            {
                ForceCheckSurgeForDistrict(district.Id);
            }
            
            // 显示检查结果
            var active = GetActiveSurges();
            Debug.Log($"[PriceEngine] 检查完成，活跃热区数量: {active.Count}");
            foreach (var surge in active)
            {
                var district = DistrictCatalog.GetDistrict(surge.DistrictId);
                var product = ProductCatalog.GetProduct(surge.GoodsId);
                Debug.Log($"[PriceEngine] 热区: {district?.Name ?? surge.DistrictId} - {product?.Name ?? surge.GoodsId} (开始: {surge.StartHour}, 结束: {surge.EndHour})");
            }
        }
        
        /// <summary>
        /// 强制检查指定街区的热区（测试用，忽略天数限制）
        /// </summary>
        private void ForceCheckSurgeForDistrict(string districtId)
        {
            // 检查是否已存在暴涨窗口（如果已存在且还未结束，不重复创建）
            if (_activeSurges.TryGetValue(districtId, out SurgeWindow existing))
            {
                // 如果窗口还在有效期内，不创建新的
                if (_state.CurrentHour <= existing.EndHour)
                {
                    Debug.Log($"[PriceEngine] 街区 {districtId} 已有活跃热区，跳过检查");
                    return;
                }
                // 如果窗口已过期，先移除
                _activeSurges.Remove(districtId);
                _state.RemoveSurgeWindow(districtId);
            }
            
            // 确保ProductCatalog已初始化
            if (ProductCatalog.GetProductCount() == 0)
            {
                ProductCatalog.Initialize();
            }
            
            // 概率触发（强制检查时忽略天数限制）
            int day = _state.CurrentDay;
            System.Random rng = new System.Random((int)(_globalSeed + districtId.GetHashCode() + day));
            float randomValue = (float)rng.NextDouble();
            
            Debug.Log($"[PriceEngine] [强制检查] 街区 {districtId}, 随机值: {randomValue:F3}, 概率: {_config.SurgeProbability}");
            
            if (randomValue < _config.SurgeProbability)
            {
                // 选择随机商品
                var allProducts = ProductCatalog.GetAllProducts();
                if (allProducts.Count == 0)
                {
                    Debug.LogWarning($"[PriceEngine] 街区 {districtId} 没有可用商品");
                    return;
                }
                
                int randomIndex = rng.Next(allProducts.Count);
                Product surgeProduct = allProducts[randomIndex];
                
                // 确定暴涨时长
                int duration = rng.Next(_config.SurgeMinHours, _config.SurgeMaxHours + 1);
                
                // 创建暴涨窗口
                SurgeWindow window = new SurgeWindow
                {
                    DistrictId = districtId,
                    GoodsId = surgeProduct.Id,
                    StartHour = _state.CurrentHour,
                    EndHour = _state.CurrentHour + duration - 1,
                    IsActive = true
                };
                
                _activeSurges[districtId] = window;
                _state.SetSurgeWindow(districtId, window);
                
                Debug.Log($"[PriceEngine] [强制检查] 街区 {districtId} 爆发 {surgeProduct.Name} 暴涨！持续 {duration} 小时");
                
                // 触发事件
                OnSurgeStarted?.Invoke(districtId, surgeProduct.Id, _config.SurgeMultiplier);
                
                // 发布EventBus事件
                if (EventBus.Instance != null)
                {
                    EventBus.Instance.Publish(new SurgeStartedEvent
                    {
                        DistrictId = districtId,
                        ProductId = surgeProduct.Id,
                        SurgeMultiplier = _config.SurgeMultiplier,
                        Source = "PriceEngine"
                    });
                }
            }
            else
            {
                Debug.Log($"[PriceEngine] [强制检查] 街区 {districtId} 未触发暴涨 (随机值: {randomValue:F3}, 概率: {_config.SurgeProbability})");
            }
        }
    }
}

