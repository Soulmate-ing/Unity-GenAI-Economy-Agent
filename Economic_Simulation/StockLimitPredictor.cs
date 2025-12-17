using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityAI.StockMarket.Model
{
	/// <summary>
	/// 股票涨停预测器 - 计算当天涨停时间和预期收益
	/// 为AI提供标准化的买卖时机建议
	/// </summary>
	public class StockLimitPredictor
	{
		/// <summary>
		/// 预测结果
		/// </summary>
		public class PredictionResult
		{
			// 基础信息
			public string StockId { get; set; }
			public int CurrentDay { get; set; }
			public int CurrentHour { get; set; }
			public decimal CurrentPrice { get; set; }
			
			// 涨停预测
			public bool WillHitLimitUp { get; set; }          // 是否会涨停
			public int PredictedLimitUpHour { get; set; }     // 预计涨停时间（小时）
			public decimal LimitUpPrice { get; set; }         // 涨停价格
			
			// 收益预测
			public decimal MaxPotentialGain { get; set; }      // 最大潜在收益率（%）
			public decimal SafeExpectedGain { get; set; }      // 保守预期收益率（%）
			public decimal RiskLevel { get; set; }             // 风险等级（0-1）
			
			// 时间建议
			public string BestBuyWindow { get; set; }          // 最佳买入窗口
			public string BestSellWindow { get; set; }         // 最佳卖出窗口
			public int RemainingGoodHours { get; set; }        // 剩余可买入小时数
			
			// 状态标记
			public PriceStatus Status { get; set; }            // 当前价格状态
			public string StatusReason { get; set; }           // 状态原因说明
		}
		
		/// <summary>
		/// 价格状态
		/// </summary>
		public enum PriceStatus
		{
			EarlyStage,        // 早期阶段 - 最佳买入期
			Rising,            // 上涨中 - 可以买入
			NearLimit,         // 接近涨停 - 谨慎买入
			LimitUp,           // 已涨停 - 不建议买入
			Falling,           // 下跌中 - 避免买入
			Stagnant           // 横盘无变化 - 观望
		}
		
		/// <summary>
		/// 预测股票当天的涨停时间和收益潜力
		/// </summary>
		public static PredictionResult Predict(
			Stock stock, 
			int currentDay, 
			int currentHour, 
			List<DailyTagEffects> dailyEffects,
			float sectorBuffStrength = 0f)
		{
			var result = new PredictionResult
			{
				StockId = stock.Id,
				CurrentDay = currentDay,
				CurrentHour = currentHour
			};
			
			if (stock.PriceSeriesCents == null || stock.PriceSeriesCents.Count == 0)
			{
				result.Status = PriceStatus.Stagnant;
				result.StatusReason = "无价格数据";
				return result;
			}
			
			int dayStartHour = (currentDay - 1) * MarketConfig.HoursPerDay;
			int dayEndHour = currentDay * MarketConfig.HoursPerDay;
			int currentHourIndex = dayStartHour + currentHour;
			
			// 确保不超出已有数据
			if (currentHourIndex >= stock.PriceSeriesCents.Count)
			{
				result.Status = PriceStatus.Stagnant;
				result.StatusReason = "超出数据范围";
				return result;
			}
			
			int currentPrice = stock.PriceSeriesCents[currentHourIndex];
			int openingPrice = stock.PriceSeriesCents[dayStartHour];
			result.CurrentPrice = currentPrice / 100m;
			result.LimitUpPrice = stock.UpperBandCents / 100m;
			
			// 1. 分析当天已有的价格走势
			var dayPrices = ExtractDayPrices(stock, dayStartHour, currentHourIndex);
			var trend = AnalyzeTrend(dayPrices);
			
			// 2. 检查当前价格状态
			decimal distanceToLimit = ((decimal)(stock.UpperBandCents - currentPrice) / stock.UpperBandCents) * 100m;
			result.Status = DetermineStatus(currentPrice, stock.UpperBandCents, trend);
			
			// 3. 预测未来价格走势（基于股票特性、板块BUFF、历史趋势）
			int predictedLimitHour = -1;
			bool willHitLimit = false;
			
			if (result.Status != PriceStatus.LimitUp)
			{
				// 计算平均每小时涨幅
				decimal avgHourlyGain = CalculateAverageHourlyGain(dayPrices);
				
				// 考虑股票特性调整
				decimal profileMultiplier = GetProfileMultiplier(stock.Profile);
				avgHourlyGain *= profileMultiplier;
				
				// 考虑板块BUFF调整
				if (sectorBuffStrength > 0.1f)
				{
					avgHourlyGain *= (1m + (decimal)sectorBuffStrength);
				}
				
				// 预测需要多少小时才能涨停
				if (avgHourlyGain > 0 && distanceToLimit > 0)
				{
					decimal priceGap = (stock.UpperBandCents - currentPrice) / 100m;
					int hoursNeeded = (int)Math.Ceiling(priceGap / (currentPrice / 100m * avgHourlyGain));
					
					predictedLimitHour = currentHour + hoursNeeded;
					
					// 检查是否在当天内涨停
					if (predictedLimitHour < MarketConfig.HoursPerDay)
					{
						willHitLimit = true;
						result.WillHitLimitUp = true;
						result.PredictedLimitUpHour = predictedLimitHour;
					}
				}
			}
			else
			{
				// 已涨停，找到涨停时间点
				predictedLimitHour = FindLimitUpHour(stock, dayStartHour, currentHourIndex);
				result.WillHitLimitUp = true;
				result.PredictedLimitUpHour = predictedLimitHour;
			}
			
			// 4. 计算收益预测
			CalculateExpectedGains(result, stock, currentPrice, openingPrice, trend, sectorBuffStrength);
			
			// 5. 生成时间建议
			GenerateTimeRecommendations(result, currentHour, predictedLimitHour, trend);
			
			// 6. 计算风险等级
			result.RiskLevel = CalculateRiskLevel(stock, trend, distanceToLimit, sectorBuffStrength);
			
			// 7. 生成状态原因说明
			result.StatusReason = GenerateStatusReason(result, distanceToLimit, trend);
			
			return result;
		}
		
		/// <summary>
		/// 提取当天的价格数据
		/// </summary>
		private static List<int> ExtractDayPrices(Stock stock, int dayStart, int currentIndex)
		{
			var prices = new List<int>();
			for (int i = dayStart; i <= currentIndex && i < stock.PriceSeriesCents.Count; i++)
			{
				prices.Add(stock.PriceSeriesCents[i]);
			}
			return prices;
		}
		
		/// <summary>
		/// 分析价格趋势
		/// </summary>
		private static TrendType AnalyzeTrend(List<int> prices)
		{
			if (prices.Count < 2) return TrendType.Flat;
			
			int risingCount = 0;
			int fallingCount = 0;
			int flatCount = 0;
			
			for (int i = 1; i < prices.Count; i++)
			{
				if (prices[i] > prices[i - 1]) risingCount++;
				else if (prices[i] < prices[i - 1]) fallingCount++;
				else flatCount++;
			}
			
			// ⭐ 增强检测：如果大部分时间价格不变，判定为横盘
			int totalChanges = risingCount + fallingCount;
			if (flatCount > prices.Count * 0.7) // 超过70%时间不变
			{
				return TrendType.Flat;
			}
			
			// 判断主导趋势
			if (risingCount > fallingCount * 1.5) return TrendType.StrongUp;
			if (risingCount > fallingCount) return TrendType.Up;
			if (fallingCount > risingCount * 1.5) return TrendType.StrongDown;
			if (fallingCount > risingCount) return TrendType.Down;
			return TrendType.Flat;
		}
		
		private enum TrendType
		{
			StrongDown,
			Down,
			Flat,
			Up,
			StrongUp
		}
		
		/// <summary>
		/// 确定当前价格状态
		/// </summary>
		private static PriceStatus DetermineStatus(int currentPrice, int upperLimit, TrendType trend)
		{
			decimal distancePercent = ((decimal)(upperLimit - currentPrice) / upperLimit) * 100m;
			
			// 已涨停（距离上限小于2%）
			if (distancePercent < 2m)
			{
				return PriceStatus.LimitUp;
			}
			
			// 接近涨停（距离上限小于10%）
			if (distancePercent < 10m)
			{
				return PriceStatus.NearLimit;
			}
			
			// ⭐ 增强检测：横盘趋势直接标记为Stagnant（不推荐）
			if (trend == TrendType.Flat)
			{
				return PriceStatus.Stagnant;
			}
			
			// 根据趋势判断
			switch (trend)
			{
				case TrendType.StrongUp:
				case TrendType.Up:
					return distancePercent > 30m ? PriceStatus.EarlyStage : PriceStatus.Rising;
				
				case TrendType.StrongDown:
				case TrendType.Down:
					return PriceStatus.Falling;
				
				default:
					return PriceStatus.Stagnant;
			}
		}
		
		/// <summary>
		/// 计算平均每小时涨幅（只计算上涨的小时）
		/// </summary>
		private static decimal CalculateAverageHourlyGain(List<int> prices)
		{
			if (prices.Count < 2) return 0m;
			
			decimal totalGain = 0m;
			int gainCount = 0;
			
			for (int i = 1; i < prices.Count; i++)
			{
				if (prices[i] > prices[i - 1])
				{
					decimal gain = ((decimal)(prices[i] - prices[i - 1]) / prices[i - 1]) * 100m;
					totalGain += gain;
					gainCount++;
				}
			}
			
			return gainCount > 0 ? totalGain / gainCount : 0m;
		}
		
		/// <summary>
		/// 根据股票特性获取涨幅倍数
		/// </summary>
		private static decimal GetProfileMultiplier(ProfileType profile)
		{
			switch (profile)
			{
				case ProfileType.Bear:
					return 0.5m;  // 熊市股涨得慢
				case ProfileType.Sideways:
					return 0.8m;  // 横盘股涨得也慢
				case ProfileType.Bull:
					return 1.2m;  // 牛市股涨得快
				case ProfileType.Moonshot:
					return 1.8m;  // 暴涨股涨得很快
				default:
					return 1.0m;
			}
		}
		
		/// <summary>
		/// 计算预期收益
		/// </summary>
		private static void CalculateExpectedGains(
			PredictionResult result, 
			Stock stock, 
			int currentPrice, 
			int openingPrice,
			TrendType trend,
			float sectorBuffStrength)
		{
			// 最大潜在收益 = 从当前价到涨停价
			if (currentPrice > 0)
			{
				result.MaxPotentialGain = ((decimal)(stock.UpperBandCents - currentPrice) / currentPrice) * 100m;
			}
			
			// 保守预期收益 = 考虑风险和趋势的调整后收益
			decimal trendMultiplier = trend switch
			{
				TrendType.StrongUp => 0.8m,
				TrendType.Up => 0.6m,
				TrendType.Flat => 0.3m,
				TrendType.Down => 0.1m,
				TrendType.StrongDown => 0.0m,
				_ => 0.5m
			};
			
			decimal buffMultiplier = sectorBuffStrength > 0.1f ? 1.2m : 1.0m;
			
			result.SafeExpectedGain = result.MaxPotentialGain * trendMultiplier * buffMultiplier;
			
			// 如果已涨停或接近涨停，保守收益很低
			if (result.Status == PriceStatus.LimitUp)
			{
				result.SafeExpectedGain = 0m;
			}
			else if (result.Status == PriceStatus.NearLimit)
			{
				result.SafeExpectedGain = Math.Min(result.SafeExpectedGain, 5m);
			}
		}
		
		/// <summary>
		/// 生成时间建议
		/// </summary>
		private static void GenerateTimeRecommendations(
			PredictionResult result,
			int currentHour,
			int predictedLimitHour,
			TrendType trend)
		{
			switch (result.Status)
			{
				case PriceStatus.LimitUp:
					result.BestBuyWindow = "已涨停，不建议买入";
					result.BestSellWindow = "已涨停，可考虑卖出";
					result.RemainingGoodHours = 0;
					break;
				
				case PriceStatus.NearLimit:
					result.BestBuyWindow = "接近涨停，风险较高";
					result.BestSellWindow = $"{currentHour}:00 立即卖出";
					result.RemainingGoodHours = Math.Max(0, predictedLimitHour - currentHour);
					break;
				
			case PriceStatus.EarlyStage:
				int safeEndHour = predictedLimitHour > 0 
					? Math.Min(predictedLimitHour - 2, currentHour + 6) 
					: currentHour + 6;
				
				// ⭐ 修复：确保结束时间大于开始时间
				if (safeEndHour <= currentHour)
				{
					safeEndHour = currentHour + 3; // 至少给3小时窗口
				}
				
				result.BestBuyWindow = $"{currentHour}:00 - {Math.Min(safeEndHour, 23)}:00（最佳期）";
				result.BestSellWindow = predictedLimitHour > 0 && predictedLimitHour <= 23
					? $"{Math.Max(predictedLimitHour - 1, currentHour + 1)}:00 - {predictedLimitHour}:00" 
					: $"{Math.Min(safeEndHour + 3, 23)}:00 左右";
				result.RemainingGoodHours = Math.Max(0, safeEndHour - currentHour);
				break;
			
			case PriceStatus.Rising:
				int conservativeEndHour = predictedLimitHour > 0 
					? Math.Min(predictedLimitHour - 1, currentHour + 4) 
					: currentHour + 4;
				
				// ⭐ 修复：确保结束时间大于开始时间
				if (conservativeEndHour <= currentHour)
				{
					conservativeEndHour = currentHour + 2; // 至少给2小时窗口
				}
				
				result.BestBuyWindow = $"{currentHour}:00 - {Math.Min(conservativeEndHour, 23)}:00";
				result.BestSellWindow = predictedLimitHour > 0 && predictedLimitHour <= 23
					? $"{predictedLimitHour}:00 前后" 
					: $"{Math.Min(conservativeEndHour + 2, 23)}:00 左右";
				result.RemainingGoodHours = Math.Max(0, conservativeEndHour - currentHour);
				break;
				
				case PriceStatus.Falling:
					result.BestBuyWindow = "下跌中，不建议买入";
					result.BestSellWindow = $"{currentHour}:00 止损";
					result.RemainingGoodHours = 0;
					break;
				
				case PriceStatus.Stagnant:
					result.BestBuyWindow = "横盘观望";
					result.BestSellWindow = "等待明确信号";
					result.RemainingGoodHours = 0;
					break;
			}
		}
		
		/// <summary>
		/// 计算风险等级
		/// </summary>
		private static decimal CalculateRiskLevel(
			Stock stock, 
			TrendType trend, 
			decimal distanceToLimit,
			float sectorBuffStrength)
		{
			decimal risk = 0.5m; // 基础风险
			
			// 距离涨停越近，风险越高
			if (distanceToLimit < 5m) risk += 0.4m;
			else if (distanceToLimit < 15m) risk += 0.2m;
			else if (distanceToLimit > 50m) risk -= 0.1m;
			
			// 趋势风险
			if (trend == TrendType.StrongDown || trend == TrendType.Down) risk += 0.3m;
			else if (trend == TrendType.StrongUp) risk -= 0.2m;
			
			// 股票特性风险
			if (stock.Profile == ProfileType.Bear) risk += 0.2m;
			else if (stock.Profile == ProfileType.Moonshot) risk += 0.1m; // 暴涨股也有风险
			
			// 板块BUFF降低风险
			if (sectorBuffStrength > 0.15f) risk -= 0.2m;
			else if (sectorBuffStrength > 0.08f) risk -= 0.1m;
			
			// 限制在0-1范围
			return Math.Max(0m, Math.Min(1m, risk));
		}
		
		/// <summary>
		/// 生成状态原因说明
		/// </summary>
		private static string GenerateStatusReason(
			PredictionResult result,
			decimal distanceToLimit,
			TrendType trend)
		{
			switch (result.Status)
			{
				case PriceStatus.EarlyStage:
					return $"距涨停还有{distanceToLimit:F1}%空间，趋势向上，是买入好时机";
				
				case PriceStatus.Rising:
					return $"上涨中，距涨停{distanceToLimit:F1}%，还有机会";
				
				case PriceStatus.NearLimit:
					return $"距涨停仅{distanceToLimit:F1}%，风险较大";
				
				case PriceStatus.LimitUp:
					return "已达到涨停价，无上涨空间";
				
				case PriceStatus.Falling:
					return "价格下跌中，不建议买入";
				
				case PriceStatus.Stagnant:
					return "价格横盘波动小，等待明确信号";
				
				default:
					return "状态未知";
			}
		}
		
		/// <summary>
		/// 找到涨停发生的小时（已涨停的情况）
		/// </summary>
		private static int FindLimitUpHour(Stock stock, int dayStart, int currentIndex)
		{
			// 从当前往回找，找到最后一次价格变化的时刻
			for (int i = currentIndex; i > dayStart; i--)
			{
				if (i < stock.PriceSeriesCents.Count && i > 0)
				{
					if (stock.PriceSeriesCents[i] != stock.PriceSeriesCents[i - 1])
					{
						return i - dayStart;
					}
				}
			}
			return 0; // 开盘就涨停
		}
		
		/// <summary>
		/// 格式化预测结果为可读文本（供AI使用）
		/// </summary>
		public static string FormatForAI(PredictionResult result)
		{
			var lines = new List<string>();
			
			lines.Add($"股票: {result.StockId}");
			lines.Add($"当前价: ¥{result.CurrentPrice:F2}, 涨停价: ¥{result.LimitUpPrice:F2}");
			lines.Add($"状态: {GetStatusText(result.Status)} - {result.StatusReason}");
			
			if (result.WillHitLimitUp)
			{
				lines.Add($"预计涨停时间: {result.PredictedLimitUpHour}:00");
			}
			
			lines.Add($"最大潜在收益: {result.MaxPotentialGain:F2}%");
			lines.Add($"保守预期收益: {result.SafeExpectedGain:F2}%");
			lines.Add($"风险等级: {GetRiskText(result.RiskLevel)}");
			lines.Add($"建议买入时间: {result.BestBuyWindow}");
			lines.Add($"建议卖出时间: {result.BestSellWindow}");
			
			if (result.RemainingGoodHours > 0)
			{
				lines.Add($"剩余可买入时间: {result.RemainingGoodHours}小时");
			}
			
			return string.Join("\n", lines);
		}
		
		private static string GetStatusText(PriceStatus status)
		{
			return status switch
			{
				PriceStatus.EarlyStage => "早期阶段★★★★★",
				PriceStatus.Rising => "上涨中★★★★",
				PriceStatus.NearLimit => "接近涨停★★",
				PriceStatus.LimitUp => "已涨停",
				PriceStatus.Falling => "下跌中",
				PriceStatus.Stagnant => "横盘观望",
				_ => "未知"
			};
		}
		
		private static string GetRiskText(decimal risk)
		{
			if (risk < 0.3m) return "低风险★";
			if (risk < 0.5m) return "中低风险★★";
			if (risk < 0.7m) return "中等风险★★★";
			if (risk < 0.85m) return "较高风险★★★★";
			return "高风险★★★★★";
		}
	}
}

