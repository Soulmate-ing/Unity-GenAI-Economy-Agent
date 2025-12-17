using System;
using System.Linq;
using System.Text.RegularExpressions;
using CityAI.Common;

namespace CityAI.AI.Utils
{
    /// <summary>
    /// 日期检测工具类
    /// 统一处理所有AI系统的日期检测逻辑
    /// </summary>
    public static class DateDetectionHelper
    {
        /// <summary>
        /// 检查是否询问未来日期
        /// </summary>
        public static bool IsAskingAboutFuture(string question)
        {
            if (string.IsNullOrEmpty(question)) return false;
            
            var questionLower = question.ToLower();
            
            // 检查是否包含未来日期关键词
            var futureKeywords = new[] { "明天", "后天", "下周", "下月", "明年", "未来" };
            if (futureKeywords.Any(keyword => questionLower.Contains(keyword)))
            {
                return true;
            }
            
            // 检查具体日期格式，并与游戏时间比较
            var parsedDate = ParseDateFromQuestion(question);
            if (parsedDate.HasValue)
            {
                return IsDateInFuture(parsedDate.Value);
            }
            
            return false;
        }
        
        /// <summary>
        /// 从问题中解析日期
        /// </summary>
        public static DateTime? ParseDateFromQuestion(string question)
        {
            try
            {
                // 匹配 "X月X日" 格式
                var match = Regex.Match(question, @"(\d+)月(\d+)日");
                if (match.Success)
                {
                    var month = int.Parse(match.Groups[1].Value);
                    var day = int.Parse(match.Groups[2].Value);
                    return new DateTime(2024, month, day); // 假设是2024年
                }
                
                // 匹配 "X月X号" 格式
                match = Regex.Match(question, @"(\d+)月(\d+)号");
                if (match.Success)
                {
                    var month = int.Parse(match.Groups[1].Value);
                    var day = int.Parse(match.Groups[2].Value);
                    return new DateTime(2024, month, day);
                }
                
                // 匹配 "X/X" 格式
                match = Regex.Match(question, @"(\d+)/(\d+)");
                if (match.Success)
                {
                    var month = int.Parse(match.Groups[1].Value);
                    var day = int.Parse(match.Groups[2].Value);
                    return new DateTime(2024, month, day);
                }
                
                // 匹配 "X-X" 格式
                match = Regex.Match(question, @"(\d+)-(\d+)");
                if (match.Success)
                {
                    var month = int.Parse(match.Groups[1].Value);
                    var day = int.Parse(match.Groups[2].Value);
                    return new DateTime(2024, month, day);
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// 检查日期是否在未来
        /// </summary>
        public static bool IsDateInFuture(DateTime targetDate)
        {
            try
            {
                // 获取当前游戏时间
                var gameTimeManager = GameTimeManager.Instance;
                if (gameTimeManager == null)
                {
                    UnityEngine.Debug.LogWarning("[DateDetectionHelper] 无法获取游戏时间管理器，使用系统时间");
                    return targetDate > DateTime.Now;
                }
                
                var currentGameDateTime = gameTimeManager.GetDateTime();
                var currentGameDate = currentGameDateTime.Date; // 只比较日期部分
                var targetDateOnly = targetDate.Date;
                
                UnityEngine.Debug.Log($"[DateDetectionHelper] 当前游戏日期: {currentGameDate:yyyy-MM-dd}, 目标日期: {targetDateOnly:yyyy-MM-dd}");
                
                return targetDateOnly > currentGameDate;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[DateDetectionHelper] 检查日期失败: {e.Message}");
                return false;
            }
        }
    }
}

