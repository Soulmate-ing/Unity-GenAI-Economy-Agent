using System;
using System.Threading.Tasks;
using UnityEngine;
using CityAI.Player.Model;
using CityAI.Lottery.Ctrl;
using CityAI.AI.Core;
using CityAI.AI.Data;
using System.Linq;
using System.Collections.Generic;
using CityAI.AI.Utils;
using CityAI.AI.Router;
using CityAI.AI.Router.Models;

namespace CityAI.AI.Handlers
{
    /// <summary>
    /// 彩票AI处理器
    /// </summary>
    public class LotteryAIHandler : IAISystemHandler
    {
        public string SystemId => "Lottery";
        
        private readonly LotteryController lotteryController;
        private readonly FortuneReplyManager replyManager;
        
        // 意图判断缓存
        private static readonly Dictionary<string, bool> intentCache = new Dictionary<string, bool>();
        private static readonly int maxCacheSize = 100;
        
        public LotteryAIHandler(LotteryController controller)
        {
            lotteryController = controller;
            replyManager = FortuneReplyManager.Instance;
        }
        
        public bool IsMatch(string question)
        {
            if (string.IsNullOrEmpty(question)) return false;
            
            // 先检查缓存
            if (intentCache.TryGetValue(question, out bool cachedResult))
            {
                // Debug.Log($"[LotteryAIHandler] 缓存命中：{question} -> {cachedResult}");
                return cachedResult;
            }
            
            // 降级到快速关键词匹配，避免AI调用阻塞
            var questionLower = question.ToLower();
            
            // 排除明显的比喻/闲聊
            if (CityAI.AI.Utils.QueryIntentHelper.IsMetaphoricalOrCasual(questionLower))
            {
                // Debug.Log($"[LotteryAIHandler] 关键词匹配：{question} -> 比喻/闲聊，不匹配");
                CacheIntent(question, false);
                return false;
            }
            
            // 检查彩票意图
            bool isMatch = CityAI.AI.Utils.QueryIntentHelper.IsLotteryIntent(questionLower);
            bool hasLotteryIntent = CityAI.AI.Utils.QueryIntentHelper.HasLotteryInvestmentIntent(questionLower);
            bool hasGeneralKeywords = CityAI.AI.Utils.QueryIntentHelper.HasLotteryGeneralKeywords(questionLower);
            
            // Debug.Log($"[LotteryAIHandler] 关键词匹配：{question} -> 彩票意图：{hasLotteryIntent}, 通用关键词：{hasGeneralKeywords}, 最终匹配：{isMatch}");
            
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
                // 未来类问题统一使用Excel话术库
                if (IsAskingAboutFuture(question))
                {
                    return ReplyHelper.GetFutureRejection();
                }

                // 检查是否要求精确预测
                if (IsAskingForPrecisePrediction(question))
                {
                    return ReplyHelper.GetPrecisePredictionRejection();
                }

                
                // 检查彩票系统是否已初始化
                if (lotteryController.CurrentDayData == null)
                {
                    Debug.LogWarning("[LotteryAIHandler] 彩票系统未初始化");
                    return "彩票系统未初始化，无法提供建议。";
                }
                
                // 直接从彩票系统获取数据
                var currentDay = CityAI.Common.GameTimeManager.Instance.CurrentDay;
                var currentHour = CityAI.Common.GameTimeManager.Instance.CurrentHour;
                
                // 获取今日彩票数据
                var todayData = lotteryController.CurrentDayData;
                var isKoiDay = todayData?.KoiInfo?.IsKoiDay ?? false;
                var koiDayTypeCode = todayData?.KoiInfo?.SelectedTypeCode ?? "";
                
                // 【新通道】调用 SelectAndRender 生成结构化 Snap
                var selectionInput = new LotterySelectionInput
                {
                    DaySeed = HandlerHelper.GenerateDaySeed(currentDay, "lottery"),
                    FaceValue = 5, // TODO: 从配置或玩家偏好获取
                    Candidates = new List<LotteryCandidate>
                    {
                        new LotteryCandidate 
                        { 
                            Id = "default", 
                            Hint = isKoiDay ? "今日锦鲤日" : "今日非锦鲤日",
                            Segment = "mid",
                            HasJackpot = isKoiDay,
                            HasSecond = false
                        }
                    }
                };
                
                var playerContext = HandlerHelper.ToPlayerContext(playerModel);
                var snap = await SelectAndRender.LotteryTop2Async(question, playerContext, selectionInput);
                
                // 转为文本返回（向后兼容）
                var reply = HandlerHelper.RenderSnapToText(snap);
                
                // 轻量校验：两种模式都跳过文本校验
                // - 兜底模式：我们自己生成的，已知安全
                // - 小模型模式：已有 Schema 校验
                // Debug.Log($"[LotteryAIHandler] 跳过文本校验（兜底模式安全/小模型有Schema校验）");

                return reply;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LotteryAIHandler] 处理失败：{e.Message}");
                return "彩票建议暂时无法提供，请稍后重试。";
            }
        }

        /// <summary>
        /// 处理AI查询并返回 Snap 对象（用于多系统统一评分）
        /// </summary>
        public async Task<Snap> HandleQueryAsSnapAsync(string question, PlayerModel playerModel)
        {
            try
            {
                // 复用逻辑：生成 selectionInput
                if (lotteryController.CurrentDayData == null)
                {
                    Debug.LogWarning("[LotteryAIHandler] 彩票系统未初始化");
                    return null;
                }
                
                var currentDay = CityAI.Common.GameTimeManager.Instance.CurrentDay;
                var todayData = lotteryController.CurrentDayData;
                var isKoiDay = todayData?.KoiInfo?.IsKoiDay ?? false;
                
                var selectionInput = new LotterySelectionInput
                {
                    DaySeed = HandlerHelper.GenerateDaySeed(currentDay, "lottery"),
                    FaceValue = 5,
                    Candidates = new List<LotteryCandidate>
                    {
                        new LotteryCandidate 
                        { 
                            Id = "default", 
                            Hint = isKoiDay ? "今日锦鲤日" : "今日非锦鲤日",
                            Segment = "mid",
                            HasJackpot = isKoiDay,
                            HasSecond = false
                        }
                    }
                };
                
                var playerContext = HandlerHelper.ToPlayerContext(playerModel);
                var snap = await SelectAndRender.LotteryTop2Async(question, playerContext, selectionInput);
                
                // Debug.Log($"[LotteryAIHandler] Snap生成成功：{snap.Title}");
                return snap;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LotteryAIHandler] Snap生成失败：{e.Message}");
                return null;
            }
        }

        private bool IsAskingAboutFuture(string question)
        {
            if (string.IsNullOrEmpty(question)) return false;
            
            // 检查相对时间关键词
            var futureKeywords = new[] { "明天", "后天", "下周", "下个月", "未来", "预测" };
            if (futureKeywords.Any(k => question.Contains(k)))
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
            return DateDetectionHelper.ParseDateFromQuestion(question).HasValue;
        }

        /// <summary>
        /// 判断日期是否在未来
        /// </summary>
        private bool IsDateInFuture(string question)
        {
            try
            {
                var parsedDate = DateDetectionHelper.ParseDateFromQuestion(question);
                if (parsedDate == null) return false;
                return DateDetectionHelper.IsDateInFuture(parsedDate.Value);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LotteryAIHandler] 检查日期失败: {e.Message}");
                return false;
            }
        }

        // 解析日期逻辑统一迁移至 DateDetectionHelper

        /// <summary>
        /// 检查是否要求精确预测
        /// </summary>
        private bool IsAskingForPrecisePrediction(string question)
        {
            if (string.IsNullOrEmpty(question)) return false;
            var questionLower = question.ToLower();
            
            // 更精确的检测模式，避免误判普通查询
            var precisePatterns = new[]
            {
                // 明确要求具体数字的短语
                "中奖.*概率", "中奖.*几率", "中奖.*多少倍", "能中.*多少", "中多少.*元",
                "赚多少.*元", "赔多少.*元", "几等奖.*中", "一等奖.*概率", "二等奖.*概率",
                
                // 明确要求精确预测的短语
                "精确.*号码", "准确.*号码", "具体.*号码", "准确.*预测",
                "精确.*预测", "具体.*预测", "准确.*中奖"
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
            var preciseKeywords = new[] { "精确", "准确", "概率", "几率", "几等奖" };
            var numberKeywords = new[] { "元", "倍", "多少", "%", "百分之" };
            
            int preciseCount = preciseKeywords.Count(k => questionLower.Contains(k));
            int numberCount = numberKeywords.Count(k => questionLower.Contains(k));
            
            // 只有同时包含精确关键词和数字关键词时才认为是精确预测
            return preciseCount >= 1 && numberCount >= 1;
        }
        
    }
}
