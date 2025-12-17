using System;
using System.Collections.Generic;
using System.Linq;

namespace CityAI.StockMarket.Model
{
	/// <summary>
	/// 股票生成库
	/// </summary>
	public static class StockLibrary
	{
		public static List<Stock> GenerateCandidates(int seed = 12345)
		{
			var rng = new Random(seed);
			var candidates = new List<Stock>(MarketConfig.CandidateStockCount);
			int idCounter = 1;
		for (int i = 0; i < MarketConfig.CandidateStockCount; i++)
		{
			var name = GenerateName(i, rng);
			var tags = PickTags(rng, 2 + rng.Next(0, 2)); // 2 or 3 tags
			var profile = VolatilityProfile.SampleProfile(rng);
			var (low, high) = VolatilityProfile.GetRangeWithJitter(profile, rng, 0.02m);
			
			// 🔥 根据股票类型设置不同的初始价格区间（更真实）
			int initial = GenerateInitialPrice(rng, profile);
			
			candidates.Add(new Stock
			{
				Id = $"S{idCounter:000}",
				Name = name,
				Tags = tags,
				InitialPriceCents = initial,
				Profile = profile,
				RtLow = low,
				RtHigh = high
			});
			idCounter++;
		}
			return candidates;
		}

		public static List<Stock> PickSessionStocks(List<Stock> candidates, int sessionCount, int seed)
		{
			var rng = new Random(seed);
			return candidates
				.OrderBy(_ => rng.Next())
				.Take(sessionCount)
				.Select(CloneWithoutSeries)
				.ToList();
		}

		private static Stock CloneWithoutSeries(Stock s)
		{
			return new Stock
			{
				Id = s.Id,
				Name = s.Name,
				Tags = new List<string>(s.Tags),
				InitialPriceCents = s.InitialPriceCents,
				Profile = s.Profile,
				RtLow = s.RtLow,
				RtHigh = s.RtHigh,
				PriceSeriesCents = new List<int>(MarketConfig.TotalHours + 1)
			};
		}

		private static string GenerateName(int index, Random rng)
		{
			// Mix of Chinese style names and generic letters
			string[] prefixes = { "鹅厂", "钢铁", "云科", "恒信", "远航", "宏达", "星链", "中芯", "华光", "盛唐", "金晟", "天工", "新能", "德信", "瑞科" };
			var prefix = prefixes[rng.Next(prefixes.Length)];
			var suffix = rng.Next(0, 2) == 0 ? ((char)('A' + (index % 26))).ToString() : (rng.Next(10, 99)).ToString();
			return $"{prefix}{suffix}";
		}

	private static List<string> PickTags(Random rng, int count)
	{
		var tags = TagCatalog.AllTags.OrderBy(_ => rng.Next()).Take(count).ToList();
		return tags;
	}
	
	/// <summary>
	/// 根据股票类型生成初始价格
	/// 优化：避免极端价格，设置合理的价格区间
	/// </summary>
	private static int GenerateInitialPrice(Random rng, ProfileType profile)
	{
		// 不同类型股票的价格区间（避免极端价格）
		int minCents, maxCents;
		
		switch (profile)
		{
			case ProfileType.Bear:
				// 熊市股票：价格中等偏高，但避免极端高价
				minCents = 1000;   // 10.00元
				maxCents = 8000;   // 80.00元
				break;
				
			case ProfileType.Sideways:
				// 横盘股票：价格中等，稳定型
				minCents = 500;    // 5.00元
				maxCents = 3000;   // 30.00元
				break;
				
			case ProfileType.Bull:
				// 牛市股票：价格适中，成长型
				minCents = 300;    // 3.00元
				maxCents = 5000;    // 50.00元
				break;
				
			case ProfileType.Moonshot:
				// 暴涨股票：低价潜力股，但避免极端低价
				minCents = 100;    // 1.00元（避免0.01元）
				maxCents = 2000;    // 20.00元
				break;
				
			default:
				minCents = 200;    // 2.00元
				maxCents = 5000;   // 50.00元
				break;
		}
		
		// 确保价格在合理范围内
		int price = rng.Next(minCents, maxCents + 1);
		if (price < MarketConfig.MinPriceCents) price = MarketConfig.MinPriceCents;
		price = BandMath.ClampToConfigBounds(price);
		return price;
	}
}
}


