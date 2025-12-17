using System;

namespace CityAI.StockMarket.Model
{
	public enum ProfileType
	{
		Bear,
		Sideways,
		Bull,
		Moonshot
	}
	/// <summary>
	/// 波动率配置
	/// </summary>
	public static class VolatilityProfile
	{
		public static ProfileType SampleProfile(Random rng)
		{
			var r = rng.NextDouble();
			if (r < MarketConfig.BearWeight)
			{
				return ProfileType.Bear;
			}
			r -= MarketConfig.BearWeight;
			if (r < MarketConfig.SidewaysWeight)
			{
				return ProfileType.Sideways;
			}
			r -= MarketConfig.SidewaysWeight;
			if (r < MarketConfig.BullWeight)
			{
				return ProfileType.Bull;
			}
			return ProfileType.Moonshot;
		}

		public static (decimal low, decimal high) GetBaseRange(ProfileType type)
		{
			switch (type)
			{
				case ProfileType.Bear: return MarketConfig.BearRange;
				case ProfileType.Sideways: return MarketConfig.SidewaysRange;
				case ProfileType.Bull: return MarketConfig.BullRange;
				case ProfileType.Moonshot: return MarketConfig.MoonshotRange;
				default: return MarketConfig.SidewaysRange;
			}
		}

		public static (decimal low, decimal high) GetRangeWithJitter(ProfileType type, Random rng, decimal jitter = 0.02m)
		{
			var baseRange = GetBaseRange(type);
			decimal lowJitter = (decimal)(rng.NextDouble() * (double)jitter * 2.0 - (double)jitter);
			decimal highJitter = (decimal)(rng.NextDouble() * (double)jitter * 2.0 - (double)jitter);
			var low = Clamp(baseRange.low + lowJitter, MarketConfig.GlobalRtMin, MarketConfig.GlobalRtMax);
			var high = Clamp(baseRange.high + highJitter, MarketConfig.GlobalRtMin, MarketConfig.GlobalRtMax);
			if (high < low)
			{
				var tmp = low; low = high; high = tmp;
			}
			return (low, high);
		}

		private static decimal Clamp(decimal v, decimal min, decimal max)
		{
			if (v < min) return min;
			if (v > max) return max;
			return v;
		}
	}
}


