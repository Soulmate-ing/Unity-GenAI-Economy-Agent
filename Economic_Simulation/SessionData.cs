using System;
using System.Collections.Generic;

namespace CityAI.StockMarket.Model
{
	[Serializable]
	public class MarketSession
	{
		public int Seed;
		public List<Stock> Stocks = new List<Stock>();
		public List<DailyTagEffects> DailyEffects = new List<DailyTagEffects>();
		public int CurrentHourIndex = 0; // 0..240

	public DailyTagEffects GetEffectsForDay(int dayIndex)
	{
		// 支持无限天数：循环使用已生成的30天数据
		if (dayIndex < 1) throw new ArgumentOutOfRangeException(nameof(dayIndex));
		
		// 使用模运算循环：第31天使用第1天的数据，第32天使用第2天的数据，以此类推
		int effectiveDay = ((dayIndex - 1) % MarketConfig.NumDays) + 1;
		return DailyEffects[effectiveDay - 1];
	}
	}

	public static class DailyEventGenerator
	{
		private struct Cat
		{
			public decimal min;
			public decimal max;
			public float weight;
			public Cat(decimal min, decimal max, float weight) { this.min = min; this.max = max; this.weight = weight; }
		}

		private static readonly Cat[] Categories = new[]
		{
			new Cat(-0.90m, -0.50m, 0.05f), // big down
			new Cat(-0.30m, -0.10m, 0.10f), // down
			new Cat(-0.10m,  0.00m, 0.20f), // small down
			new Cat(-0.02m,  0.02m, 0.20f), // flat
			new Cat( 0.01m,  0.10m, 0.20f), // small up
			new Cat( 0.10m,  0.30m, 0.15f), // up
			new Cat( 0.50m,  0.90m, 0.10f), // big up
		};

		public static List<DailyTagEffects> GenerateDailyEffects(int seed)
		{
			var rng = new Random(seed * 31 + 7);
			var days = new List<DailyTagEffects>(MarketConfig.NumDays);
			for (int d = 1; d <= MarketConfig.NumDays; d++)
			{
				int count = 3 + rng.Next(0, 3); // 3-5 tags
				var chosen = new HashSet<string>();
				var effects = new DailyTagEffects { DayIndex = d };
				while (chosen.Count < count)
				{
					var tag = TagCatalog.AllTags[rng.Next(TagCatalog.AllTags.Count)];
					if (chosen.Add(tag))
					{
						var (emin, emax) = SampleCategoryRange(rng);
						var value = SampleUniformDecimal(rng, emin, emax);
						effects.TagToEffect[tag] = value;
					}
				}
				days.Add(effects);
			}
			return days;
		}

		private static (decimal, decimal) SampleCategoryRange(Random rng)
		{
			float total = 0f;
			for (int i = 0; i < Categories.Length; i++) total += Categories[i].weight;
			var r = (float)rng.NextDouble() * total;
			for (int i = 0; i < Categories.Length; i++)
			{
				r -= Categories[i].weight;
				if (r <= 0f) return (Categories[i].min, Categories[i].max);
			}
			return (0m, 0m);
		}

		private static decimal SampleUniformDecimal(Random rng, decimal min, decimal max)
		{
			var t = (decimal)rng.NextDouble();
			return min + (max - min) * t;
		}
	}
}


