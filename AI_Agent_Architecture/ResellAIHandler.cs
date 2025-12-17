using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using CityAI.Player.Model;
using CityAI.AI.Core;
using CityAI.AI.Utils;
using CityAI.AI.Router;
using CityAI.AI.Router.Models;
using CityAI.ResaleSystem.PriceEngine;
using CityAI.ResaleSystem.Model;
using CityAI.Common;

namespace CityAI.AI.Handlers
{
    /// <summary>
    /// 倒卖系统 AI 处理器
    /// </summary>
    public class ResellAIHandler : IAISystemHandler
    {
        public string SystemId => "Resell";
        
        // 意图判断缓存
        private static readonly Dictionary<string, bool> intentCache = new Dictionary<string, bool>();
        private static readonly int maxCacheSize = 100;
        
        public bool IsMatch(string question)
        {
            if (string.IsNullOrEmpty(question)) return false;
            
            // 先检查缓存
            if (intentCache.TryGetValue(question, out bool cachedResult))
            {
                // Debug.Log($"[ResellAIHandler] 缓存命中：{question} -> {cachedResult}");
                return cachedResult;
            }
            
            // 快速关键词匹配
            var q = question.ToLowerInvariant();
            bool isMatch = q.Contains("倒卖") 
                || q.Contains("热区") 
                || q.Contains("摆摊") 
                || q.Contains("进货") 
                || q.Contains("路线") 
                || q.Contains("批发")
                || q.Contains("卖货")
                || q.Contains("暴涨")
                || q.Contains("售卖");
            
            // Debug.Log($"[ResellAIHandler] 关键词匹配：{question} -> {isMatch}");
            
            CacheIntent(question, isMatch);
            return isMatch;
        }
        
        /// <summary>
        /// 缓存意图判断结果
        /// </summary>
        private static void CacheIntent(string question, bool isIntent)
        {
            if (intentCache.Count >= maxCacheSize)
            {
                // 移除最旧的缓存项
                var oldestKey = intentCache.Keys.First();
                intentCache.Remove(oldestKey);
            }
            
            intentCache[question] = isIntent;
        }
        
        public async Task<string> HandleQueryAsync(string question, PlayerModel playerModel)
        {
            try
            {
                // Debug.Log("[ResellAIHandler] 开始处理倒卖询问");
                
                // 检查是否询问未来日期
                if (DateDetectionHelper.IsAskingAboutFuture(question))
                {
                    // Debug.Log("[ResellAIHandler] 检测到未来日期询问，返回拒答");
                    return ReplyHelper.GetFutureRejection();
                }
                
                // 1. 构建选择输入
                var selectionInput = BuildResellSelectionInput(playerModel);
                
                // 如果没有热区数据，返回提示
                if (selectionInput.TodaySurges == null || selectionInput.TodaySurges.Count == 0)
                {
                    Debug.Log("[ResellAIHandler] 当前无活跃热区");
                    return "今日暂无明显热区，建议观望或稳健投资其他渠道。";
                }
                
                // 2. 调用选择链
                var playerContext = HandlerHelper.ToPlayerContext(playerModel);
                var snap = await SelectAndRender.ResellTop2Async(question, playerContext, selectionInput);
                
                // 3. 转为文本返回
                var response = HandlerHelper.RenderSnapToText(snap);
                // Debug.Log($"[ResellAIHandler] Snap回复：{response}");
                
                // 4. 轻量校验（灰度开启时跳过）
                // if (!SelectionConfig.EnableSelection)
                // {
                //     Debug.Log("[ResellAIHandler] 兜底模式：跳过文本校验");
                // }
                // else
                // {
                //     Debug.Log("[ResellAIHandler] 小模型模式：跳过文本校验（已有 Schema 校验）");
                // }
                
                return response;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ResellAIHandler] 处理失败：{e.Message}\n{e.StackTrace}");
                return "倒卖建议暂时无法提供，请稍后重试。";
            }
        }
        
        public async Task<Snap> HandleQueryAsSnapAsync(string question, PlayerModel playerModel)
        {
            try
            {
                var selectionInput = BuildResellSelectionInput(playerModel);
                
                // 如果没有热区数据，返回兜底Snap
                if (selectionInput.TodaySurges == null || selectionInput.TodaySurges.Count == 0)
                {
                    return new Snap
                    {
                        Id = CityAI.AI.Router.Models.SystemId.Resell,
                        Title = "今日无明显热区",
                        Threshold = 100,
                        FundMin = 100,
                        FundMax = 5000,
                        Capacity = 1000,
                        Turnover = 0.8,
                        Edge = 0.0,
                        Virality = 0.5,
                        Actions = new List<ActionItem>()
                    };
                }
                
                var playerContext = HandlerHelper.ToPlayerContext(playerModel);
                var snap = await SelectAndRender.ResellTop2Async(question, playerContext, selectionInput);
                
                // Debug.Log($"[ResellAIHandler] Snap生成成功：{snap.Title}");
                return snap;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ResellAIHandler] Snap生成失败：{e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 构建倒卖选择输入
        /// </summary>
        private ResellSelectionInput BuildResellSelectionInput(PlayerModel playerModel)
        {
            // 获取PriceEngine实例
            var priceEngine = GameObject.FindObjectOfType<PriceEngine>();
            if (priceEngine == null)
            {
                Debug.LogWarning("[ResellAIHandler] 未找到PriceEngine，返回空热区列表");
                return new ResellSelectionInput
                {
                    DaySeed = HandlerHelper.GenerateDaySeed(GameTimeManager.Instance?.CurrentDay ?? 1, "resell"),
                    PlayerZone = "中心街区",
                    Capacity = 10,
                    TodaySurges = new List<Surge>()
                };
            }
            
            // ⭐ 确保 PriceEngine 的时间与 GameTimeManager 同步
            if (GameTimeManager.Instance != null)
            {
                int gameTimeDay = GameTimeManager.Instance.CurrentDay;
                int gameTimeHour = GameTimeManager.Instance.TotalHours % 24;
                int priceEngineDay = priceEngine.GetCurrentDay();
                int priceEngineHour = priceEngine.GetCurrentHour();
                
                // 如果时间不同步，强制同步
                if (gameTimeDay != priceEngineDay || Math.Abs(gameTimeHour - priceEngineHour) > 1)
                {
                    // Debug.Log($"[ResellAIHandler] 检测到时间不同步：GameTimeManager={gameTimeDay}天{gameTimeHour}时, PriceEngine={priceEngineDay}天{priceEngineHour}时，强制同步");
                    priceEngine.UpdateTime(GameTimeManager.Instance.TotalHours, gameTimeDay);
                }
            }
            
            // 获取当前时间
            int currentDay = priceEngine.GetCurrentDay();
            int currentHour = priceEngine.GetCurrentHour();
            
            // 获取活跃的暴涨窗口
            var activeWindows = priceEngine.GetActiveSurges();
            
            // ⭐ 如果没有活跃热区，但游戏时间在第3天或之后，尝试强制检查
            if (activeWindows.Count == 0 && currentDay >= 3)
            {
                // Debug.Log($"[ResellAIHandler] 当前无活跃热区，尝试强制检查（当前：Day {currentDay}, Hour {currentHour}）");
                priceEngine.ForceCheckAllSurges();
                activeWindows = priceEngine.GetActiveSurges();
                // Debug.Log($"[ResellAIHandler] 强制检查后，活跃热区数量：{activeWindows.Count}");
            }
            
            // 转换为AI需要的Surge格式
            var todaySurges = ConvertToSurges(activeWindows, currentHour);
            
            // ⭐ 记录每个热区的详细信息，用于调试AI选择原因（仅在开发模式下）
            // if (todaySurges.Count > 0)
            // {
            //     Debug.Log($"[ResellAIHandler] 热区详情（供AI选择）：");
            //     for (int i = 0; i < todaySurges.Count; i++)
            //     {
            //         var surge = todaySurges[i];
            //         Debug.Log($"  热区{i + 1}: {surge.Zone} - {surge.ProductName} ({surge.Category}), 强度={surge.StrengthTag}, 剩余时间={surge.RemainingHours}小时");
            //     }
            // }
            
            // 获取玩家位置（临时硬编码，后续可从PlayerModel获取）
            string playerZone = "中心街区";  // TODO: 从PlayerModel或PlayerController获取
            
            // 获取货箱容量（临时硬编码，后续可从PlayerModel获取）
            int capacity = 10;  // TODO: 从PlayerModel的Vehicle系统获取
            
            // Debug.Log($"[ResellAIHandler] 构建输入完成：Day={currentDay}, Hour={currentHour}, 热区数量={todaySurges.Count}");
            
            return new ResellSelectionInput
            {
                DaySeed = HandlerHelper.GenerateDaySeed(currentDay, "resell"),
                PlayerZone = playerZone,
                Capacity = capacity,
                TodaySurges = todaySurges
            };
        }
        
        /// <summary>
        /// 将PriceEngine的SurgeWindow转换为AI需要的Surge格式
        /// </summary>
        private List<Surge> ConvertToSurges(List<SurgeWindow> windows, int currentHour)
        {
            var surges = new List<Surge>();
            
            foreach (var window in windows)
            {
                // 获取街区信息
                var district = DistrictCatalog.GetDistrict(window.DistrictId);
                var districtName = district?.Name ?? window.DistrictId;
                
                // 获取商品信息
                var product = ProductCatalog.GetProduct(window.GoodsId);
                var categoryName = product?.Category.ToString() ?? "未知类别";
                var productName = product?.Name ?? "未知商品";  // ⭐ 获取具体商品名称
                
                // 计算剩余小时数
                int remainingHours = window.EndHour - currentHour + 1;
                
                // 计算时间窗口
                string timeWindow = CalculateTimeWindow(window.StartHour, window.EndHour, currentHour);
                
                // 计算强度标签（基于剩余时间和倍数）
                string strengthTag = CalculateStrengthTag(window, currentHour);
                
                // 计算距离（临时简化，固定值）
                int distance = CalculateDistance("中心街区", districtName);
                
                surges.Add(new Surge
                {
                    Id = $"SURGE_{window.DistrictId}_{window.GoodsId}",
                    Zone = districtName,
                    Window = timeWindow,
                    StrengthTag = strengthTag,
                    Category = categoryName,
                    Distance = distance,
                    RemainingHours = remainingHours,  // ⭐ 添加剩余小时数，供AI生成标题使用
                    ProductName = productName  // ⭐ 添加具体商品名称，供AI生成标题使用
                });
            }
            
            return surges;
        }
        
        /// <summary>
        /// 计算时间窗口描述
        /// </summary>
        private string CalculateTimeWindow(int startHour, int endHour, int currentHour)
        {
            int remainingHours = endHour - currentHour + 1;
            
            if (remainingHours <= 2)
                return "即将结束";
            else if (remainingHours <= 6)
                return "短窗口";
            else if (remainingHours <= 12)
                return "中等窗口";
            else
                return "长窗口";
        }
        
        /// <summary>
        /// 计算强度标签
        /// </summary>
        private string CalculateStrengthTag(SurgeWindow window, int currentHour)
        {
            // 基于剩余时间判断强度
            // 剩余时间越长，机会越好（strong）
            int remainingHours = window.EndHour - currentHour + 1;
            
            if (remainingHours >= 12)
                return "strong";  // 剩余时间充足
            else if (remainingHours >= 4)
                return "mid";     // 剩余时间中等
            else
                return "weak";    // 即将结束
        }
        
        /// <summary>
        /// 计算距离（简化版本）
        /// </summary>
        private int CalculateDistance(string fromZone, string toZone)
        {
            if (fromZone == toZone) return 0;
            
            // TODO: 实现真实的地图距离计算
            // 临时使用固定值
            var distanceMap = new Dictionary<(string, string), int>
            {
                { ("中心街区", "商业区"), 2 },
                { ("中心街区", "工业区"), 3 },
                { ("中心街区", "住宅区"), 2 },
                { ("中心街区", "科技园"), 4 }
            };
            
            if (distanceMap.TryGetValue((fromZone, toZone), out int distance))
                return distance;
            
            if (distanceMap.TryGetValue((toZone, fromZone), out distance))
                return distance;
            
            return 3;  // 默认距离
        }
    }
}

