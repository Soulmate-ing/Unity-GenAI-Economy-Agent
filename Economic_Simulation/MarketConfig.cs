using System;

namespace CityAI.StockMarket.Model
{
	public enum SectorEffectMode
	{
		Hourly,
		DailyOnce
	}
	/// <summary>
	/// 市场配置
	/// </summary>
	public static class MarketConfig
	{
		public const int NumDays = 30;
		public const int HoursPerDay = 24;
		public const int TotalHours = NumDays * HoursPerDay; // 240

		public const int CandidateStockCount = 80; // >= 60
		public const int SessionStockCount = 20; // per session

	public const int MinPriceCents = 100; // 1.00元，避免极端低价
	public const int MaxPriceCents = 100_000_000; // safety cap: 1,000,000.00
	public const float LowerBandMultiplier = 0.5f; // 最低价格 = 初始价 * 0.5（更宽松）
	public const float UpperBandMultiplier = 5.0f; // 最高价格 = 初始价 * 5.0（提高上限，让股票在一天内有更多上涨空间）
	public const int DefaultInitialPriceMinCents = 200; // 2.00元（更多低价股）
	public const int DefaultInitialPriceMaxCents = 10000; // 100.00元（更多高价股）

	public const decimal GlobalRtMin = 0.5m;
	public const decimal GlobalRtMax = 1.5m;

	// 🔥 增强波动率配置（增加股票变化幅度）
	public static readonly (decimal low, decimal high) BearRange = (0.88m, 0.95m);      // 熊市：每小时-5%~-12%（增强）
	public static readonly (decimal low, decimal high) SidewaysRange = (0.94m, 1.06m);  // 横盘：每小时-6%~+6%（增强）
	public static readonly (decimal low, decimal high) BullRange = (1.05m, 1.12m);      // 牛市：每小时+5%~+12%（增强）
	public static readonly (decimal low, decimal high) MoonshotRange = (1.08m, 1.20m);  // 暴涨：每小时+8%~+20%（增强）

	// 🔥 优化股票类型分布（减少横盘股，增加活跃股票）
	public const float BearWeight = 0.25f;      // 25%熊市
	public const float SidewaysWeight = 0.15f;  // 15%横盘（减少）
	public const float BullWeight = 0.40f;      // 40%牛市（增加）
	public const float MoonshotWeight = 0.20f;  // 20%暴涨（增加）

		public const decimal SectorSumMinClamp = -0.99m; // clamp(Σ effects, -0.99, +inf)

		public static SectorEffectMode SectorEffectApplication = SectorEffectMode.Hourly;

		// 股票推荐时间窗口配置（小时数）
		// 推荐时间段从当前小时开始，持续指定的小时数
		// 例如：RecommendationWindowHours = 6 表示从当前小时开始的6小时窗口
		public const int RecommendationWindowHours = 6;

		public static int PriceToCents(decimal priceInYuan)
		{
			var rawCents = priceInYuan * 100m;
			if (rawCents <= MinPriceCents) return MinPriceCents;
			if (rawCents >= MaxPriceCents) return MaxPriceCents;
			var rounded = Math.Round(rawCents, MidpointRounding.AwayFromZero);
			var cents = (int)rounded;
			if (cents < MinPriceCents) cents = MinPriceCents;
			if (cents > MaxPriceCents) cents = MaxPriceCents;
			return cents;
		}

		public static decimal CentsToYuan(int cents)
		{
			if (cents < MinPriceCents) cents = MinPriceCents;
			if (cents > MaxPriceCents) cents = MaxPriceCents;
			return cents / 100m;
		}
	}
}


