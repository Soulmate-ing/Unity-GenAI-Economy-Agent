using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityAI.StockMarket.Model
{
	/// <summary>
	/// 股票排序器 - 根据预期收益和风险对股票进行综合评分排序
	/// 帮助AI选出最优的推荐股票
	/// </summary>
	public class StockRanker
	{
		/// <summary>
		/// 排序后的股票推荐
		/// </summary>
		public class RankedStock
		{
			public string StockId { get; set; }
			public string StockName { get; set; }
			public decimal Score { get; set; }                  // 综合评分（0-100）
			public StockLimitPredictor.PredictionResult Prediction { get; set; }
			public string Recommendation { get; set; }          // 推荐等级
			public string ActionAdvice { get; set; }            // 操作建议
		}
		
		/// <summary>
		/// 排序权重配置
		/// </summary>
		public class RankingWeights
		{
			public decimal ExpectedGainWeight { get; set; } = 0.4m;    // 预期收益权重
			public decimal RiskWeight { get; set; } = 0.3m;             // 风险权重（负向）
			public decimal TimeWindowWeight { get; set; } = 0.2m;       // 时间窗口权重
			public decimal TrendWeight { get; set; } = 0.1m;            // 趋势权重
		}
		
		/// <summary>
		/// 对股票列表进行综合评分和排序
		/// </summary>
		/// <param name="predictions">所有股票的预测结果</param>
		/// <param name="stocks">股票对象列表</param>
		/// <param name="weights">排序权重（可选）</param>
		/// <param name="topN">返回前N个（0表示返回全部）</param>
		/// <returns>排序后的股票列表</returns>
		public static List<RankedStock> RankStocks(
			List<StockLimitPredictor.PredictionResult> predictions,
			List<Stock> stocks,
			RankingWeights weights = null,
			int topN = 0)
		{
			if (weights == null)
			{
				weights = new RankingWeights();
			}
			
			var ranked = new List<RankedStock>();
			
			foreach (var prediction in predictions)
			{
				var stock = stocks.FirstOrDefault(s => s.Id == prediction.StockId);
				if (stock == null) continue;
				
				// 计算综合评分
				decimal score = CalculateScore(prediction, weights);
				
				// 生成推荐等级和操作建议
				var (recommendation, advice) = GenerateRecommendation(prediction, score);
				
				ranked.Add(new RankedStock
				{
					StockId = prediction.StockId,
					StockName = stock.Name,
					Score = score,
					Prediction = prediction,
					Recommendation = recommendation,
					ActionAdvice = advice
				});
			}
			
			// 按评分降序排序
			ranked = ranked.OrderByDescending(r => r.Score).ToList();
			
			// 如果指定了topN，只返回前N个
			if (topN > 0 && ranked.Count > topN)
			{
				ranked = ranked.Take(topN).ToList();
			}
			
			return ranked;
		}
		
		/// <summary>
		/// 计算综合评分（0-100分）
		/// </summary>
		private static decimal CalculateScore(
			StockLimitPredictor.PredictionResult prediction,
			RankingWeights weights)
		{
			decimal score = 0m;
			
			// 1. 预期收益得分（0-100）
			decimal gainScore = Math.Min(100m, prediction.SafeExpectedGain * 2m); // 50%收益=100分
			score += gainScore * weights.ExpectedGainWeight;
			
			// 2. 风险得分（风险越低，得分越高）
			decimal riskScore = (1m - prediction.RiskLevel) * 100m;
			score += riskScore * weights.RiskWeight;
			
			// 3. 时间窗口得分（剩余时间越多，得分越高）
			decimal timeScore = Math.Min(100m, prediction.RemainingGoodHours * 10m); // 10小时=100分
			score += timeScore * weights.TimeWindowWeight;
			
			// 4. 状态趋势得分
			decimal trendScore = GetStatusScore(prediction.Status);
			score += trendScore * weights.TrendWeight;
			
			return Math.Max(0m, Math.Min(100m, score));
		}
		
		/// <summary>
		/// 根据价格状态获取得分
		/// </summary>
		private static decimal GetStatusScore(StockLimitPredictor.PriceStatus status)
		{
			return status switch
			{
				StockLimitPredictor.PriceStatus.EarlyStage => 100m,    // 最佳
				StockLimitPredictor.PriceStatus.Rising => 80m,         // 良好
				StockLimitPredictor.PriceStatus.NearLimit => 40m,      // 一般
				StockLimitPredictor.PriceStatus.Stagnant => 15m,       // ⭐ 降低评分：横盘不动
				StockLimitPredictor.PriceStatus.Falling => 10m,        // 很差
				StockLimitPredictor.PriceStatus.LimitUp => 0m,         // 最差
				_ => 50m
			};
		}
		
		/// <summary>
		/// 生成推荐等级和操作建议
		/// </summary>
		private static (string recommendation, string advice) GenerateRecommendation(
			StockLimitPredictor.PredictionResult prediction,
			decimal score)
		{
			string recommendation;
			string advice;
			
			if (score >= 75m)
			{
				recommendation = "强烈推荐★★★★★";
				advice = GenerateBuyAdvice(prediction, "强力买入");
			}
			else if (score >= 60m)
			{
				recommendation = "推荐★★★★";
				advice = GenerateBuyAdvice(prediction, "建议买入");
			}
			else if (score >= 45m)
			{
				recommendation = "谨慎推荐★★★";
				advice = GenerateBuyAdvice(prediction, "可适量买入");
			}
			else if (score >= 30m)
			{
				recommendation = "观望★★";
				advice = "持币观望，等待更好时机";
			}
			else
			{
				recommendation = "不推荐★";
				advice = prediction.Status == StockLimitPredictor.PriceStatus.Falling 
					? "建议止损或避免买入" 
					: "不建议买入，寻找其他机会";
			}
			
			return (recommendation, advice);
		}
		
		/// <summary>
		/// 生成买入建议
		/// </summary>
		private static string GenerateBuyAdvice(
			StockLimitPredictor.PredictionResult prediction,
			string actionPrefix)
		{
			var parts = new List<string>();
			parts.Add(actionPrefix);
			
			if (prediction.SafeExpectedGain > 0)
			{
				parts.Add($"预期收益{prediction.SafeExpectedGain:F1}%");
			}
			
			if (prediction.RemainingGoodHours > 0)
			{
				parts.Add($"剩余{prediction.RemainingGoodHours}小时");
			}
			
			if (prediction.WillHitLimitUp && prediction.PredictedLimitUpHour > 0)
			{
				parts.Add($"预计{prediction.PredictedLimitUpHour}:00涨停");
			}
			
			return string.Join("，", parts);
		}
		
		/// <summary>
		/// 格式化排序结果为可读文本（供AI使用）
		/// </summary>
		public static string FormatTopRecommendations(List<RankedStock> rankedStocks, int showCount = 3)
		{
			var lines = new List<string>();
			lines.Add("=== 股票推荐排行 ===\n");
			
			int count = Math.Min(showCount, rankedStocks.Count);
			for (int i = 0; i < count; i++)
			{
				var stock = rankedStocks[i];
				lines.Add($"【第{i + 1}名】{stock.StockName} ({stock.StockId})");
				lines.Add($"  评分: {stock.Score:F1}/100");
				lines.Add($"  推荐: {stock.Recommendation}");
				lines.Add($"  建议: {stock.ActionAdvice}");
				lines.Add($"  当前价: ¥{stock.Prediction.CurrentPrice:F2}");
				lines.Add($"  预期收益: {stock.Prediction.SafeExpectedGain:F1}% (最高{stock.Prediction.MaxPotentialGain:F1}%)");
				lines.Add($"  风险等级: {FormatRisk(stock.Prediction.RiskLevel)}");
				lines.Add($"  买入窗口: {stock.Prediction.BestBuyWindow}");
				
				if (stock.Prediction.WillHitLimitUp)
				{
					lines.Add($"  涨停预测: {stock.Prediction.PredictedLimitUpHour}:00");
				}
				
				lines.Add(""); // 空行分隔
			}
			
			return string.Join("\n", lines);
		}
		
		/// <summary>
		/// 生成简洁的推荐摘要（用于AI快速决策）
		/// </summary>
		public static string GenerateBriefSummary(List<RankedStock> rankedStocks)
		{
			if (rankedStocks == null || rankedStocks.Count == 0)
			{
				return "当前无推荐股票";
			}
			
			// 统计各评分段的股票数量
			int strongBuy = rankedStocks.Count(r => r.Score >= 75);
			int buy = rankedStocks.Count(r => r.Score >= 60 && r.Score < 75);
			int cautious = rankedStocks.Count(r => r.Score >= 45 && r.Score < 60);
			int watch = rankedStocks.Count(r => r.Score >= 30 && r.Score < 45);
			int avoid = rankedStocks.Count(r => r.Score < 30);
			
			var summary = new List<string>();
			
			if (strongBuy > 0)
			{
				var topStock = rankedStocks.First();
				summary.Add($"强推{strongBuy}只，首选{topStock.StockName}(预期收益{topStock.Prediction.SafeExpectedGain:F1}%)");
			}
			
			if (buy > 0 && strongBuy == 0)
			{
				var topStock = rankedStocks.First(r => r.Score >= 60);
				summary.Add($"推荐{buy}只，建议{topStock.StockName}(收益{topStock.Prediction.SafeExpectedGain:F1}%)");
			}
			
			if (strongBuy == 0 && buy == 0 && cautious > 0)
			{
				summary.Add($"市场一般，{cautious}只可谨慎参与");
			}
			
			if (strongBuy == 0 && buy == 0 && cautious == 0)
			{
				summary.Add("市场较差，建议观望为主");
			}
			
			return string.Join("；", summary);
		}
		
		private static string FormatRisk(decimal risk)
		{
			if (risk < 0.3m) return "低★";
			if (risk < 0.5m) return "中低★★";
			if (risk < 0.7m) return "中等★★★";
			if (risk < 0.85m) return "较高★★★★";
			return "高★★★★★";
		}
		
		/// <summary>
		/// 过滤出可买入的股票（排除已涨停、下跌、横盘等不适合买入的股票）
		/// </summary>
		public static List<RankedStock> FilterBuyable(List<RankedStock> rankedStocks, decimal minScore = 45m)
		{
			return rankedStocks.Where(r => 
				r.Score >= minScore &&
				r.Prediction.Status != StockLimitPredictor.PriceStatus.LimitUp &&
				r.Prediction.Status != StockLimitPredictor.PriceStatus.Falling &&
				r.Prediction.RemainingGoodHours > 0
			).ToList();
		}
		
		/// <summary>
		/// 找出最适合卖出的持仓股票（接近涨停或下跌的股票）
		/// </summary>
		public static List<RankedStock> FindSellCandidates(List<RankedStock> rankedStocks)
		{
			return rankedStocks.Where(r => 
				r.Prediction.Status == StockLimitPredictor.PriceStatus.LimitUp ||
				r.Prediction.Status == StockLimitPredictor.PriceStatus.NearLimit ||
				r.Prediction.Status == StockLimitPredictor.PriceStatus.Falling
			).OrderByDescending(r => 
				r.Prediction.Status == StockLimitPredictor.PriceStatus.LimitUp ? 3 :
				r.Prediction.Status == StockLimitPredictor.PriceStatus.NearLimit ? 2 : 1
			).ToList();
		}
	}
}

