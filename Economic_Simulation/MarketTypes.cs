using System;
using System.Collections.Generic;

namespace CityAI.StockMarket.Model
{
	[Serializable]
	public class Stock
	{
		public string Id;
		public string Name;
		public List<string> Tags = new List<string>();

		// 初始价格（分）
		public int InitialPriceCents;
		public ProfileType Profile;
		public decimal RtLow;
		public decimal RtHigh;

		public List<int> PriceSeriesCents = new List<int>(MarketConfig.TotalHours + 1);
		public int LowerBandCents;
		public int UpperBandCents;
	}

	[Serializable]
	public class DailyTagEffects
	{
		public int DayIndex; // 1..NumDays
		public Dictionary<string, decimal> TagToEffect = new Dictionary<string, decimal>();
	}

	public static class PriceMath
	{
		/// <summary>
		/// 应用每小时价格更新
		/// 改进：确保价格至少变化1分（除非因子正好为1.0）
		/// </summary>
		public static int ApplyHourlyUpdate(int currentPriceCents, decimal rt, decimal sd)
		{
			if (currentPriceCents < MarketConfig.MinPriceCents) currentPriceCents = MarketConfig.MinPriceCents;
			var priceYuan = currentPriceCents / 100m;
			var factor = rt * (1m + sd);
			var nextYuan = priceYuan * factor;
			// Clamp before casting to int to avoid OverflowException
			var rawCents = nextYuan * 100m;
			if (rawCents <= MarketConfig.MinPriceCents) return MarketConfig.MinPriceCents;
			if (rawCents >= MarketConfig.MaxPriceCents) return MarketConfig.MaxPriceCents;
			var rounded = Math.Round(rawCents, MidpointRounding.AwayFromZero);
			var nextCents = (int)rounded;
			if (nextCents < MarketConfig.MinPriceCents) nextCents = MarketConfig.MinPriceCents;
			if (nextCents > MarketConfig.MaxPriceCents) nextCents = MarketConfig.MaxPriceCents;
			
		// 🔥 增强：确保价格有明显变化（增加波动性）
		// 使用更严格的阈值，确保股票有可见的变化
		if (nextCents == currentPriceCents)
		{
			const decimal threshold = 0.001m;  // 0.1%阈值（更严格）
			
			// 如果因子明显 > 1.0，至少涨1分；如果明显 < 1.0，至少跌1分
			if (factor > 1.0m + threshold)
			{
				nextCents = currentPriceCents + 1;
			}
			else if (factor < 1.0m - threshold)
			{
				nextCents = Math.Max(currentPriceCents - 1, MarketConfig.MinPriceCents);
			}
			// 如果factor非常接近1.0，强制至少变化1分（增加活跃度）
			else if (Math.Abs(factor - 1.0m) > 0.0001m) // 有微小变化时
			{
				nextCents = factor > 1.0m ? currentPriceCents + 1 : Math.Max(currentPriceCents - 1, MarketConfig.MinPriceCents);
			}
		}
			
			// 再次限制范围
			if (nextCents < MarketConfig.MinPriceCents) nextCents = MarketConfig.MinPriceCents;
			if (nextCents > MarketConfig.MaxPriceCents) nextCents = MarketConfig.MaxPriceCents;
			
			return nextCents;
		}

		public static decimal ClampSectorSum(decimal sum)
		{
			return sum < MarketConfig.SectorSumMinClamp ? MarketConfig.SectorSumMinClamp : sum;
		}
	}

	public static class BandMath
	{
		public static int ClampToConfigBounds(int cents)
		{
			if (cents < MarketConfig.MinPriceCents) return MarketConfig.MinPriceCents;
			if (cents > MarketConfig.MaxPriceCents) return MarketConfig.MaxPriceCents;
			return cents;
		}

		public static int ComputeLowerBand(int initialCents)
		{
			var multiplier = (decimal)MarketConfig.LowerBandMultiplier;
			var lower = (decimal)initialCents * multiplier;
			var rounded = (int)Math.Round(lower, MidpointRounding.AwayFromZero);
			return ClampToConfigBounds(Math.Max(rounded, MarketConfig.MinPriceCents));
		}

		public static int ComputeUpperBand(int initialCents)
		{
			var multiplier = (decimal)MarketConfig.UpperBandMultiplier;
			var upper = (decimal)initialCents * multiplier;
			var rounded = (int)Math.Round(upper, MidpointRounding.AwayFromZero);
			return ClampToConfigBounds(Math.Max(rounded, MarketConfig.MinPriceCents));
		}
	}
}


