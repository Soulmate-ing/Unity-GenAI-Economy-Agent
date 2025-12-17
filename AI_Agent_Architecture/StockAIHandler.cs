using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using CityAI.Player.Model;
using CityAI.StockMarket.Ctrl;
using CityAI.StockMarket.Model;
using CityAI.AI.Core;
using CityAI.AI.Data;
using CityAI.AI.Utils;
using CityAI.AI.Router;
using CityAI.AI.Router.Models;

namespace CityAI.AI.Handlers
{
    /// <summary>
    /// 股票AI处理器
    /// </summary>
    public class StockAIHandler : IAISystemHandler
    {
        public string SystemId => "Stocks";
        
        private readonly StockMarketController stockController;
        private readonly FortuneReplyManager replyManager;
        
        // 意图判断缓存
        private static readonly Dictionary<string, bool> intentCache = new Dictionary<string, bool>();
        private static readonly int maxCacheSize = 100;
        
        public StockAIHandler(StockMarketController controller)
        {
            stockController = controller;
            replyManager = FortuneReplyManager.Instance;
        }
        
        public bool IsMatch(string question)
        {
            if (string.IsNullOrEmpty(question)) return false;
            
            // 先检查缓存
            if (intentCache.TryGetValue(question, out bool cachedResult))
            {
                // Debug.Log($"[StockAIHandler] 缓存命中：{question} -> {cachedResult}");
                return cachedResult;
            }
            
            // 降级到快速关键词匹配，避免AI调用阻塞
            var questionLower = question.ToLower();
            
            // 排除明显的比喻/闲聊
            if (CityAI.AI.Utils.QueryIntentHelper.IsMetaphoricalOrCasual(questionLower))
            {
                // Debug.Log($"[StockAIHandler] 关键词匹配：{question} -> 比喻/闲聊，不匹配");
                CacheIntent(question, false);
                return false;
            }
            
            // 检查投资意图
            bool isMatch = CityAI.AI.Utils.QueryIntentHelper.IsStockIntent(questionLower);
            // bool hasInvestmentIntent = CityAI.AI.Utils.QueryIntentHelper.HasStockInvestmentIntent(questionLower);
            // bool hasGeneralKeywords = CityAI.AI.Utils.QueryIntentHelper.HasStockGeneralKeywords(questionLower);
            
            // Debug.Log($"[StockAIHandler] 关键词匹配：{question} -> 投资意图：{hasInvestmentIntent}, 通用关键词：{hasGeneralKeywords}, 最终匹配：{isMatch}");
            
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
            // Debug.Log($"[StockAIHandler] 开始处理问题：{question}");
            
            // 检查股票系统是否已初始化
            if (stockController?.Session == null)
            {
                Debug.LogWarning("[StockAIHandler] 股票系统未初始化");
                return "股票系统未初始化，无法提供建议。";
            }

            // 检查是否询问未来日期
            if (IsAskingAboutFuture(question))
            {
                // Debug.Log("[StockAIHandler] 检测到未来日期询问，返回拒答");
                return ReplyHelper.GetFutureRejection();
            }

            // 检查是否要求精确点位或收益
            if (IsAskingForPrecisePrediction(question))
            {
                // Debug.Log("[StockAIHandler] 检测到精确预测询问，返回拒答");
                return ReplyHelper.GetPrecisePredictionRejection();
            }


            try
            {
                // 提取股票系统数据
                var stockData = ExtractStockSystemData();
                
                // ⭐ 使用新的涨停预测系统 - 为每只股票生成精确预测
                var predictions = new List<StockLimitPredictor.PredictionResult>();
                foreach (var stockInfo in stockData.stocks)
                {
                    var stock = stockController.Session.Stocks.FirstOrDefault(s => s.Id == stockInfo.ticker);
                    if (stock != null)
                    {
                        // 计算该股票的板块BUFF强度
                        float sectorBuffStrength = 0f;
                        foreach (var buff in stockData.buffs)
                        {
                            if (stock.Tags.Contains(buff.sector) && buff.direction == "up")
                            {
                                sectorBuffStrength += buff.magnitude;
                            }
                        }
                        
                        // 使用预测器计算涨停时间和收益
                        var prediction = StockLimitPredictor.Predict(
                            stock, 
                            stockData.currentDay, 
                            stockData.currentHour,
                            stockController.Session.DailyEffects,
                            sectorBuffStrength
                        );
                        
                        predictions.Add(prediction);
                        
                        // 记录预测结果供调试（仅在需要时启用）
                        // Debug.Log($"[StockAIHandler] {stock.Id} 预测: 状态={prediction.Status}, " +
                        //     $"预期收益={prediction.SafeExpectedGain:F1}%, 风险={prediction.RiskLevel:F2}, " +
                        //     $"买入窗口={prediction.BestBuyWindow}");
                    }
                }
                
                // ⭐ 使用排序器对股票进行综合评分排序
                var rankedStocks = StockRanker.RankStocks(
                    predictions,
                    stockController.Session.Stocks,
                    weights: null, // 使用默认权重
                    topN: 0 // 返回全部，后续再筛选
                );
                
                // 记录排序结果供调试（仅在需要时启用）
                // Debug.Log($"[StockAIHandler] 股票排序完成，总计{rankedStocks.Count}只股票");
                // if (rankedStocks.Count > 0)
                // {
                //     Debug.Log($"[StockAIHandler] 最佳推荐：{rankedStocks[0].StockName} " +
                //         $"(评分={rankedStocks[0].Score:F1}, 预期收益={rankedStocks[0].Prediction.SafeExpectedGain:F1}%)");
                //     
                //     // 输出完整的推荐摘要
                //     var summary = StockRanker.GenerateBriefSummary(rankedStocks);
                //     Debug.Log($"[StockAIHandler] 市场摘要：{summary}");
                // }
                
                // 转换为AI所需的Ticker格式（保留原有接口兼容性）
                var tickersWithWindow = new List<Ticker>();
                foreach (var ranked in rankedStocks)
                {
                    var stock = stockController.Session.Stocks.FirstOrDefault(s => s.Id == ranked.StockId);
                    if (stock == null) continue;
                    
                    // ⭐ 增强过滤：根据评分和状态决定是否Avoid
                    bool shouldAvoid = ranked.Score < 30m || 
                        ranked.Prediction.Status == StockLimitPredictor.PriceStatus.LimitUp ||
                        ranked.Prediction.Status == StockLimitPredictor.PriceStatus.Falling ||
                        ranked.Prediction.Status == StockLimitPredictor.PriceStatus.Stagnant; // ⭐ 新增：过滤横盘不动
                    
                    tickersWithWindow.Add(new Ticker
                    {
                        Code = ranked.StockId,
                        Sector = stock.Tags?.FirstOrDefault() ?? "未分类",
                        Avoid = shouldAvoid,
                        Window = ranked.Prediction.BestBuyWindow, // 使用预测的买入窗口
                        ProfileType = stock.Profile.ToString(),
                        VolatilityLow = (float)stock.RtLow,
                        VolatilityHigh = (float)stock.RtHigh
                    });
                }
                
                var selectionInput = new StockSelectionInput
                {
                    DaySeed = HandlerHelper.GenerateDaySeed(stockData.currentDay, "stocks"),
                    Buffs = stockData.buffs.Select(b => new Buff
                    {
                        Sector = b.sector,
                        Direction = b.direction,
                        Window = b.window,
                        Strength = (int)(b.magnitude * 100),
                        Confidence = 80 // TODO: 从实际数据计算
                    }).ToList(),
                    Tickers = tickersWithWindow
                };
                
                var playerContext = HandlerHelper.ToPlayerContext(playerModel);
                var snap = await SelectAndRender.StockTop2Async(question, playerContext, selectionInput);
                
                // 转为文本返回（向后兼容）
                var response = HandlerHelper.RenderSnapToText(snap);

                // 轻量校验：两种模式都跳过文本校验
                // - 兜底模式：我们自己生成的，已知安全
                // - 小模型模式：已有 Schema 校验
                // Debug.Log($"[StockAIHandler] 跳过文本校验（兜底模式安全/小模型有Schema校验）");

                return response;
            }
            catch (Exception e)
            {
                Debug.LogError($"[StockAIHandler] 处理失败：{e.Message}");
                return "股票建议暂时无法提供，请稍后重试。";
            }
        }

        /// <summary>
        /// 处理AI查询并返回 Snap 对象（用于多系统统一评分）
        /// </summary>
        public async Task<Snap> HandleQueryAsSnapAsync(string question, PlayerModel playerModel)
        {
            try
            {
                // 复用逻辑：提取股票系统数据
                var stockData = ExtractStockSystemData();
                
                // ⭐ 使用新的涨停预测系统（与HandleQueryAsync保持一致）
                var predictions = new List<StockLimitPredictor.PredictionResult>();
                foreach (var stockInfo in stockData.stocks)
                {
                    var stock = stockController.Session.Stocks.FirstOrDefault(s => s.Id == stockInfo.ticker);
                    if (stock != null)
                    {
                        // 计算该股票的板块BUFF强度
                        float sectorBuffStrength = 0f;
                        foreach (var buff in stockData.buffs)
                        {
                            if (stock.Tags.Contains(buff.sector) && buff.direction == "up")
                            {
                                sectorBuffStrength += buff.magnitude;
                            }
                        }
                        
                        // 使用预测器计算涨停时间和收益
                        var prediction = StockLimitPredictor.Predict(
                            stock, 
                            stockData.currentDay, 
                            stockData.currentHour,
                            stockController.Session.DailyEffects,
                            sectorBuffStrength
                        );
                        
                        predictions.Add(prediction);
                    }
                }
                
                // ⭐ 使用排序器对股票进行综合评分排序
                var rankedStocks = StockRanker.RankStocks(
                    predictions,
                    stockController.Session.Stocks,
                    weights: null,
                    topN: 0
                );
                
                // 转换为AI所需的Ticker格式
                var tickersWithWindow = new List<Ticker>();
                foreach (var ranked in rankedStocks)
                {
                    var stock = stockController.Session.Stocks.FirstOrDefault(s => s.Id == ranked.StockId);
                    if (stock == null) continue;
                    
                    // ⭐ 增强过滤：根据评分和状态决定是否Avoid
                    bool shouldAvoid = ranked.Score < 30m || 
                        ranked.Prediction.Status == StockLimitPredictor.PriceStatus.LimitUp ||
                        ranked.Prediction.Status == StockLimitPredictor.PriceStatus.Falling ||
                        ranked.Prediction.Status == StockLimitPredictor.PriceStatus.Stagnant; // ⭐ 新增：过滤横盘不动
                    
                    tickersWithWindow.Add(new Ticker
                    {
                        Code = ranked.StockId,
                        Sector = stock.Tags?.FirstOrDefault() ?? "未分类",
                        Avoid = shouldAvoid,
                        Window = ranked.Prediction.BestBuyWindow,
                        ProfileType = stock.Profile.ToString(),
                        VolatilityLow = (float)stock.RtLow,
                        VolatilityHigh = (float)stock.RtHigh
                    });
                }
                
                var selectionInput = new StockSelectionInput
                {
                    DaySeed = HandlerHelper.GenerateDaySeed(stockData.currentDay, "stocks"),
                    Buffs = stockData.buffs.Select(b => new Buff
                    {
                        Sector = b.sector,
                        Direction = b.direction,
                        Window = b.window,
                        Strength = (int)(b.magnitude * 100),
                        Confidence = 80
                    }).ToList(),
                    Tickers = tickersWithWindow
                };
                
                var playerContext = HandlerHelper.ToPlayerContext(playerModel);
                var snap = await SelectAndRender.StockTop2Async(question, playerContext, selectionInput);
                
                // Debug.Log($"[StockAIHandler] Snap生成成功：{snap.Title}");
                return snap;
            }
            catch (Exception e)
            {
                Debug.LogError($"[StockAIHandler] Snap生成失败：{e.Message}");
                return null;
            }
        }
        
        private bool IsAskingAboutFuture(string question)
        {
            // 检查相对时间关键词
            var futureKeywords = new[] { "明天", "后天", "下周", "下个月", "未来", "预测" };
            if (futureKeywords.Any(keyword => question.Contains(keyword)))
            {
                return true;
            }
            
            // 检查具体日期
            if (IsAskingAboutSpecificDate(question))
            {
                return IsDateInFuture(question);
            }
            
            return false;
        }

        /// <summary>
        /// 检查是否询问具体日期（统一使用公共工具解析）
        /// </summary>
        private bool IsAskingAboutSpecificDate(string question)
        {
            if (string.IsNullOrEmpty(question)) return false;
            return CityAI.AI.Utils.DateDetectionHelper.ParseDateFromQuestion(question).HasValue;
        }

        /// <summary>
        /// 判断日期是否在未来
        /// </summary>
        private bool IsDateInFuture(string question)
        {
            try
            {
                // 解析问题中的日期并委托给公共工具
                var parsedDate = CityAI.AI.Utils.DateDetectionHelper.ParseDateFromQuestion(question);
                if (parsedDate == null) return false;
                return CityAI.AI.Utils.DateDetectionHelper.IsDateInFuture(parsedDate.Value);
            }
            catch (Exception e)
            {
                Debug.LogError($"[StockAIHandler] 检查日期失败: {e.Message}");
                return false;
            }
        }

        // 解析日期逻辑统一迁移至 DateDetectionHelper

        /// <summary>
        /// 检查是否要求精确点位或收益预测
        /// </summary>
        private bool IsAskingForPrecisePrediction(string question)
        {
            if (string.IsNullOrEmpty(question)) return false;
            var questionLower = question.ToLower();
            
            // 更精确的检测模式，避免误判普通查询
            var precisePatterns = new[]
            {
                // 明确要求具体数字的短语
                "涨到.*元", "跌到.*元", "涨到.*块", "跌到.*块",
                "能赚.*元", "能赚.*块", "赚多少.*元", "亏多少.*元",
                "目标价.*元", "止损价.*元", "买入价.*元", "卖出价.*元",
                "涨.*个点", "跌.*个点", "涨.*%", "跌.*%",
                
                // 明确要求精确预测的短语
                "精确.*预测", "准确.*预测", "具体.*点位", "准确.*点位",
                "精确.*价格", "准确.*价格", "具体.*收益", "准确.*收益"
            };
            
            // 检查是否包含明确的精确预测意图
            foreach (var pattern in precisePatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(questionLower, pattern))
                {
                    return true;
                }
            }
            
            // 检查是否包含多个精确关键词组合（降低误判）
            var preciseKeywords = new[] { "精确", "准确", "点位", "目标价", "止损价" };
            var numberKeywords = new[] { "元", "块", "点", "%", "百分之" };
            
            int preciseCount = preciseKeywords.Count(k => questionLower.Contains(k));
            int numberCount = numberKeywords.Count(k => questionLower.Contains(k));
            
            // 只有同时包含精确关键词和数字关键词时才认为是精确预测
            return preciseCount >= 1 && numberCount >= 1;
        }
        
        /// <summary>
        /// 检查当日价格变化情况，判断是否还有上涨潜力
        /// </summary>
        /// <returns>是否还有上涨潜力</returns>
        private bool CheckDayPriceChange(CityAI.StockMarket.Model.Stock stock, int currentDay, int currentHour, 
            out decimal dayChangePercent, out bool isNearUpperBand, out int noChangeHours)
        {
            dayChangePercent = 0m;
            isNearUpperBand = false;
            noChangeHours = 0;
            
            if (stock.PriceSeriesCents == null || stock.PriceSeriesCents.Count < 2)
            {
                return true; // 没有数据，假设有潜力
            }
            
            int dayStartHour = (currentDay - 1) * MarketConfig.HoursPerDay;
            int currentHourIndex = dayStartHour + currentHour;
            int analysisEndHour = Math.Min(currentHourIndex, stock.PriceSeriesCents.Count - 1);
            
            if (analysisEndHour <= dayStartHour)
            {
                return true; // 当天刚开始，假设有潜力
            }
            
            int openingPrice = stock.PriceSeriesCents[dayStartHour];
            int currentPrice = stock.PriceSeriesCents[analysisEndHour];
            
            // 计算当日涨跌幅
            if (openingPrice > 0)
            {
                dayChangePercent = ((decimal)(currentPrice - openingPrice) / openingPrice) * 100m;
            }
            
            // 检查是否接近价格上限（95%以上）
            isNearUpperBand = currentPrice >= stock.UpperBandCents * 0.95m;
            
            // 统计最近几小时无变化的次数（检查是否有上涨动力）
            noChangeHours = 0;
            int recentHoursToCheck = Math.Min(3, analysisEndHour - dayStartHour); // 检查最近3小时
            for (int i = 0; i < recentHoursToCheck; i++)
            {
                int hourIndex = analysisEndHour - i;
                if (hourIndex > dayStartHour && hourIndex < stock.PriceSeriesCents.Count)
                {
                    int priceAtHour = stock.PriceSeriesCents[hourIndex];
                    int prevPriceAtHour = stock.PriceSeriesCents[hourIndex - 1];
                    if (priceAtHour == prevPriceAtHour)
                    {
                        noChangeHours++;
                    }
                }
            }
            
            // 判断是否还有上涨潜力：
            // 1. 如果已经接近上限（95%以上），且最近多小时无变化，说明已经涨停
            if (isNearUpperBand && noChangeHours >= 3)
            {
                return false; // 已经涨停
            }
            
            // 2. 如果最近3小时都无变化，且当天已经涨了很多，可能没有潜力
            if (noChangeHours >= 3 && dayChangePercent > 5m)
            {
                return false; // 已经涨了很多但不再变化
            }
            
            // 3. 如果当天涨幅很大（>100%），且最近无变化，可能已经涨到位
            if (dayChangePercent > 100m && noChangeHours >= 2)
            {
                return false;
            }
            
            // 4. 如果接近上限（95%以上），即使还有变化，也认为潜力有限
            if (isNearUpperBand)
            {
                return false; // 接近上限，上涨空间很小
            }
            
            // 5. 如果当天跌了，但有板块BUFF，可能还有潜力（不在这里判断，由BUFF逻辑判断）
            
            return true; // 其他情况认为还有潜力
        }
        
        /// <summary>
        /// 计算股票实际有效买入时间段（考虑当前状态和未来潜力）
        /// 关键：返回的是"未来可买入且有上涨潜力的时间段"，而不是"历史上涨时间段"
        /// 优化：结合板块BUFF和股票特性，预测更准确的未来上涨时间段
        /// </summary>
        private string CalculateEffectiveTimeWindow(CityAI.StockMarket.Model.Stock stock, int currentDay, int currentHour, 
            List<StockBuff> sectorBuffs = null)
        {
            if (stock.PriceSeriesCents == null || stock.PriceSeriesCents.Count < 2) return "全天";
            
            int dayStartHour = (currentDay - 1) * MarketConfig.HoursPerDay;
            int dayEndHour = currentDay * MarketConfig.HoursPerDay;
            int currentHourIndex = dayStartHour + currentHour;
            int analysisEndHour = Math.Min(currentHourIndex, stock.PriceSeriesCents.Count - 1);
            
            if (analysisEndHour <= dayStartHour) return "全天"; // 当天刚开始，假设全天有机会
            
            int currentPrice = stock.PriceSeriesCents[analysisEndHour];
            int openingPrice = stock.PriceSeriesCents[dayStartHour];
            
            // 检查是否已经涨停或接近涨停
            bool isNearUpperBand = currentPrice >= stock.UpperBandCents * 0.95m;
            
            // 检查最近几小时是否还在变化
            int noChangeHours = 0;
            int recentHoursToCheck = Math.Min(5, analysisEndHour - dayStartHour); // 检查最近5小时
            for (int i = 0; i < recentHoursToCheck; i++)
            {
                int hourIndex = analysisEndHour - i;
                if (hourIndex > dayStartHour && hourIndex < stock.PriceSeriesCents.Count)
                {
                    int priceAtHour = stock.PriceSeriesCents[hourIndex];
                    int prevPriceAtHour = stock.PriceSeriesCents[hourIndex - 1];
                    if (priceAtHour == prevPriceAtHour)
                    {
                        noChangeHours++;
                    }
                }
            }
            
            // 如果已经涨停或接近涨停，且最近多小时无变化，说明已经涨停
            if (isNearUpperBand && noChangeHours >= 3)
            {
                // 找出最后有变化的小时（涨停前的时刻）
                int lastChangeHour = -1;
                for (int h = analysisEndHour; h > dayStartHour; h--)
                {
                    if (h < stock.PriceSeriesCents.Count && h > 0)
                    {
                        int priceAtHour = stock.PriceSeriesCents[h];
                        int prevPriceAtHour = stock.PriceSeriesCents[h - 1];
                        if (priceAtHour != prevPriceAtHour)
                        {
                            lastChangeHour = h - dayStartHour;
                            break;
                        }
                    }
                }
                
                if (lastChangeHour >= 0)
                {
                    return $"已涨停（最后变化：{lastChangeHour}:00）";
                }
                else
                {
                    return "已涨停";
                }
            }
            
            // 如果当天涨幅很大（>100%），且最近多小时无变化，可能已经涨停
            decimal dayChangePercent = openingPrice > 0 ? ((decimal)(currentPrice - openingPrice) / openingPrice) * 100m : 0m;
            if (dayChangePercent > 100m && noChangeHours >= 3)
            {
                // 找出最后有变化的小时
                int lastChangeHour = -1;
                for (int h = analysisEndHour; h > dayStartHour; h--)
                {
                    if (h < stock.PriceSeriesCents.Count && h > 0)
                    {
                        int priceAtHour = stock.PriceSeriesCents[h];
                        int prevPriceAtHour = stock.PriceSeriesCents[h - 1];
                        if (priceAtHour != prevPriceAtHour)
                        {
                            lastChangeHour = h - dayStartHour;
                            break;
                        }
                    }
                }
                
                if (lastChangeHour >= 0)
                {
                    return $"可能已涨停（最后变化：{lastChangeHour}:00）";
                }
                else
                {
                    return "可能已涨停";
                }
            }
            
            // 分析历史上涨模式，预测未来可能的上涨时间段
            List<int> risingHours = new List<int>();
            
            // 找出当天所有上涨的小时（用于分析模式）
            for (int h = dayStartHour + 1; h <= analysisEndHour; h++)
            {
                if (h < stock.PriceSeriesCents.Count)
                {
                    int priceAtHour = stock.PriceSeriesCents[h];
                    int prevPriceAtHour = stock.PriceSeriesCents[h - 1];
                    if (priceAtHour > prevPriceAtHour)
                    {
                        risingHours.Add(h - dayStartHour);
                    }
                }
            }
            
            // 计算当天剩余时间
            int remainingHours = dayEndHour - currentHourIndex - 1;
            
            // 检查是否有板块BUFF支撑（如果有传入）
            bool hasStrongSectorBuff = false;
            float sectorBuffStrength = 0f;
            if (sectorBuffs != null)
            {
                foreach (var buff in sectorBuffs)
                {
                    if (stock.Tags.Contains(buff.sector) && buff.direction == "up")
                    {
                        sectorBuffStrength += buff.magnitude;
                        if (buff.magnitude > 0.1f) // BUFF强度超过10%
                        {
                            hasStrongSectorBuff = true;
                        }
                    }
                }
            }
            
            // 计算距离价格上限的空间（用于判断是否还有上涨潜力）
            decimal distanceToUpperBand = ((decimal)(stock.UpperBandCents - currentPrice) / stock.UpperBandCents) * 100m;
            bool hasRoomToGrow = distanceToUpperBand > 5m; // 距离上限还有5%以上空间
            
            // 如果已经有上涨数据，分析模式并预测未来时间段
            if (risingHours.Count > 0)
            {
                // 找出连续上涨的最大时间段
                risingHours.Sort();
                int maxStart = risingHours[0];
                int maxEnd = risingHours[0];
                int maxLength = 1;
                int currentStart = risingHours[0];
                int currentEnd = risingHours[0];
                
                for (int i = 1; i < risingHours.Count; i++)
                {
                    if (risingHours[i] == currentEnd + 1)
                    {
                        currentEnd = risingHours[i];
                        if (currentEnd - currentStart + 1 > maxLength)
                        {
                            maxLength = currentEnd - currentStart + 1;
                            maxStart = currentStart;
                            maxEnd = currentEnd;
                        }
                    }
                    else
                    {
                        currentStart = risingHours[i];
                        currentEnd = risingHours[i];
                    }
                }
                
                // 如果当前还在上涨时间段内，且还有上涨空间
                if (remainingHours > 0 && currentHour <= maxEnd && hasRoomToGrow)
                {
                    // 如果有板块BUFF支撑，预测到当天结束或更长
                    if (hasStrongSectorBuff)
                    {
                        int endHour = Math.Min(23, dayEndHour - dayStartHour - 1);
                        return $"{currentHour}:00 - {endHour}:00";
                    }
                    else
                    {
                        // 没有强BUFF，保守预测剩余上涨时间段
                        int endHour = Math.Min(Math.Min(maxEnd + 2, currentHour + remainingHours), dayEndHour - dayStartHour - 1);
                        return $"{currentHour}:00 - {endHour}:00";
                    }
                }
                // 如果已经过了主要上涨时间段，但还有剩余时间和上涨空间
                else if (remainingHours > 0 && currentHour < dayEndHour - dayStartHour - 1 && hasRoomToGrow)
                {
                    // 如果有板块BUFF支撑，预测未来更长时间段
                    if (hasStrongSectorBuff)
                    {
                        // 强BUFF支撑，预测到当天结束或至少6-8小时
                        int predictedEnd = Math.Min(Math.Min(23, currentHour + 8), dayEndHour - dayStartHour - 1);
                        return $"{currentHour}:00 - {predictedEnd}:00";
                    }
                    else
                    {
                        // 没有强BUFF，基于历史模式预测未来3-5小时
                        int predictedEnd = Math.Min(currentHour + 5, dayEndHour - dayStartHour - 1);
                        return $"{currentHour}:00 - {predictedEnd}:00";
                    }
                }
                else if (maxLength >= 3)
                {
                    // 已经过了可买入时间或没有上涨空间，返回历史上涨时间段（用于参考）
                    return $"{maxStart}:00 - {maxEnd + 1}:00（已过）";
                }
            }
            
            // 如果没有历史上涨数据，但还有剩余时间和上涨空间
            if (remainingHours > 0 && hasRoomToGrow)
            {
                // 如果有板块BUFF支撑，预测更长时间段
                if (hasStrongSectorBuff)
                {
                    // 强BUFF支撑，预测到当天结束或至少6小时
                    int predictedEnd = Math.Min(Math.Min(23, currentHour + 6), dayEndHour - dayStartHour - 1);
                    return $"{currentHour}:00 - {predictedEnd}:00";
                }
                else
                {
                    // 没有强BUFF，保守预测未来3-4小时
                    int predictedEnd = Math.Min(currentHour + 4, dayEndHour - dayStartHour - 1);
                    return $"{currentHour}:00 - {predictedEnd}:00";
                }
            }
            
            // 如果没有上涨空间，返回"已过"或"全天"（保守策略）
            if (!hasRoomToGrow)
            {
                return "已接近上限";
            }
            
            // 默认返回全天（保守策略）
            return "全天";
        }
        
        private StockSystemData ExtractStockSystemData()
        {
            // 直接从股票系统获取数据
            var session = stockController.Session;
            var currentDay = CityAI.Common.GameTimeManager.Instance.CurrentDay;
            var currentHour = CityAI.Common.GameTimeManager.Instance.CurrentHour;
            
            // 获取当天的BUFF效果
            var dayEffects = session.GetEffectsForDay(currentDay);
            
            // 构建BUFF列表（为每个股票计算实际有效时间段）
            var buffs = new List<StockBuff>();
            foreach (var kvp in dayEffects.TagToEffect)
            {
                var direction = kvp.Value > 0 ? "up" : (kvp.Value < 0 ? "down" : "flat");
                var magnitude = Mathf.Abs((float)kvp.Value);
                
                // 先设为"全天"，后续会根据实际股票价格变化计算
                string windowText = "全天";
                
                buffs.Add(new StockBuff
                {
                    sector = kvp.Key,
                    direction = direction,
                    magnitude = magnitude,
                    window = windowText
                });
            }
            
            // 构建基于BUFF数据的推荐
            var recommendations = GenerateRecommendationsFromBuffs(buffs, session.Stocks, currentDay, currentHour);
            
            // 构建股票列表
            var stocks = new List<StockInfo>();
            foreach (var stock in session.Stocks)
            {
                stocks.Add(new StockInfo
                {
                    ticker = stock.Id,
                    name = stock.Name,
                    tags = stock.Tags
                });
            }
            
            // 构建股票标签映射
            var mapping = new Dictionary<string, List<string>>();
            foreach (var stock in session.Stocks)
            {
                mapping[stock.Id] = stock.Tags;
            }
            
            return new StockSystemData
            {
                buffs = buffs,
                stocks = stocks,
                mapping = mapping,
                recommendations = recommendations,
                currentTime = $"{currentDay}天 {currentHour}:00",
                currentDay = currentDay,
                currentHour = currentHour,
                recentVolatilities = new List<CityAI.AI.Core.StockVolatilityData>(), // 暂时为空
                portfolioValue = 0 // 暂时为0
            };
        }
        
        /// <summary>
        /// 基于BUFF数据生成推荐清单
        /// </summary>
        private List<StockRecommendation> GenerateRecommendationsFromBuffs(
            List<StockBuff> buffs, 
            List<CityAI.StockMarket.Model.Stock> stocks,
            int currentDay,
            int currentHour)
        {
            var recommendations = new List<StockRecommendation>();
            
            // 创建BUFF字典
            var buffDict = new Dictionary<string, StockBuff>();
            foreach (var buff in buffs)
            {
                buffDict[buff.sector] = buff;
            }
            
            foreach (var stock in stocks)
            {
                string action = "watch";
                string confidence = "low";
                string reason = "";
                
                // 计算股票的综合得分（详细debug输出）
                float totalScore = 0f;
                int tagCount = 0;
                
                foreach (var tag in stock.Tags)
                {
                    if (buffDict.ContainsKey(tag))
                    {
                        var buff = buffDict[tag];
                        float score = buff.direction == "up" ? buff.magnitude : -buff.magnitude;
                        totalScore += score;
                        tagCount++;
                    }
                }
                
                float avgScore = tagCount > 0 ? totalScore / tagCount : 0f;
                
                if (tagCount > 0)
                {
                    if (avgScore > 0.05f) // 正面效果超过5%
                    {
                        action = "buy";
                        confidence = "high";
                        reason = $"板块利好{avgScore:P1}";
                    }
                    else if (avgScore < -0.05f) // 负面效果超过5%
                    {
                        action = "avoid";
                        confidence = "high";
                        reason = $"板块利空{avgScore:P1}";
                    }
                    else if (Math.Abs(avgScore) > 0.02f) // 轻微波动
                    {
                        action = "watch";
                        confidence = "medium";
                        reason = $"板块波动{avgScore:P1}";
                    }
                    else
                    {
                        action = "watch";
                        confidence = "low";
                        reason = "板块平稳";
                    }
                }
                else
                {
                    action = "watch";
                    confidence = "low";
                    reason = "无板块数据";
                }
                
                // 检查当日价格变化情况（关键：判断是否还有上涨潜力）
                bool hasRisingPotential = CheckDayPriceChange(stock, currentDay, currentHour, out decimal dayChangePercent, out bool isNearUpperBand, out int noChangeHours);
                
                // 如果当天价格没有变化或已经接近上限，降低推荐优先级
                if (!hasRisingPotential)
                {
                    if (action == "buy")
                    {
                        // 如果当天价格没有变化或已经涨太多不再变化，改为watch或avoid
                        if (noChangeHours >= 3 || isNearUpperBand)
                        {
                            action = "avoid";
                            reason = $"当天价格无变化({dayChangePercent:+0.00;-0.00;0.00}%)或已接近上限";
                            confidence = "low";
                        }
                        else if (noChangeHours >= 1)
                        {
                            // 最近有1-2小时没变化，降低置信度
                            if (confidence == "high") confidence = "medium";
                            reason += $" (注意：当天涨幅{dayChangePercent:+0.00;-0.00;0.00}%，但最近{noChangeHours}小时无变化)";
                        }
                    }
                }
                
                // 考虑股票特性调整推荐
                // 根据股票特性调整推荐强度
                if (stock.Profile == ProfileType.Bear && avgScore > 0)
                {
                    // 熊市股票即使有板块BUFF，也可能不涨或涨幅有限
                    if (confidence == "high") confidence = "medium";
                }
                else if (stock.Profile == ProfileType.Moonshot && avgScore > 0)
                {
                    // 暴涨股票配合板块BUFF，潜力更大（但前提是还有上涨空间）
                    if (confidence == "medium" && hasRisingPotential) confidence = "high";
                }
                else if (stock.Profile == ProfileType.Sideways && avgScore > 0.1f)
                {
                    // 横盘股票需要更强的板块BUFF才有意义
                    if (avgScore < 0.15f && confidence == "high") confidence = "medium";
                }
                
                // 计算实际有效上涨时间段（传入板块BUFF信息以便更准确预测）
                string effectiveWindow = CalculateEffectiveTimeWindow(stock, currentDay, currentHour, buffs);
                
                // 如果时间段显示"已涨停"或"可能已涨停"，强制改为avoid
                if (effectiveWindow.Contains("已涨停") || effectiveWindow.Contains("可能已涨停"))
                {
                    action = "avoid";
                    reason = effectiveWindow;
                    confidence = "low";
                }
                
                recommendations.Add(new StockRecommendation
                {
                    ticker = stock.Id,
                    name = stock.Name,
                    action = action,
                    window = effectiveWindow, // 使用计算出的实际有效时间段
                    reason = reason,
                    confidence = confidence
                });
            }
            
            // 按置信度和动作排序
            recommendations.Sort((a, b) =>
            {
                // 优先排除avoid的股票
                if (a.action == "avoid" && b.action != "avoid") return 1;
                if (a.action != "avoid" && b.action == "avoid") return -1;
                if (a.action == "buy" && b.action != "buy") return -1;
                if (a.action != "buy" && b.action == "buy") return 1;
                if (b.confidence == "high" && a.confidence != "high") return 1;
                if (a.confidence == "high" && b.confidence != "high") return -1;
                return 0;
            });
            
            // 过滤掉avoid的股票
            var validRecommendations = recommendations.Where(r => r.action != "avoid").Take(3).ToList();
            return validRecommendations;
        }
        
        // 未使用的完整Prompt与相关辅助方法按指示移除，当前仅保留简化版提示词
        
        
    }
}

