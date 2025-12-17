using System;
using System.Linq;
using CityAI.AI.Config;

namespace CityAI.AI.Utils
{
	public struct VerifyPolicy
	{
		public bool TodayOnly;
		public bool DirectionalRequired;
		public bool BanPreciseNumbers;
	}

	public static class Verifier
	{
		private static readonly string[] DefaultFutureHints = {"明天","后天","下周","下月","明年","未来"};
		private static readonly string[] DefaultDirectionalWords = {"上涨","下跌","转弱","偏暖","观望","谨慎参与"};
		private static readonly string[] DefaultNumericTokens = {"元","块","%","百分之","点","收益","利润","概率","几率","目标价","止损价","买入价","卖出价"};

		public static bool Run(string text, string systemId, VerifyPolicy policy)
		{
			if (string.IsNullOrWhiteSpace(text)) return false;
			var lower = text.ToLowerInvariant();

            var cfg = AIValidationConfig.Get();
            var futureHints = cfg != null && cfg.futureHints != null && cfg.futureHints.Length > 0 ? cfg.futureHints : DefaultFutureHints;
            var directional = cfg != null && cfg.stockDirectionalWords != null && cfg.stockDirectionalWords.Length > 0 ? cfg.stockDirectionalWords : DefaultDirectionalWords;
            var numeric = cfg != null && cfg.forbiddenTokens != null && cfg.forbiddenTokens.Length > 0 ? cfg.forbiddenTokens : DefaultNumericTokens;

			if (policy.TodayOnly)
			{
				if (futureHints.Any(h => text.Contains(h))) return false;
			}
			if (policy.DirectionalRequired)
			{
				if (!directional.Any(w => text.Contains(w))) return false;
			}
			if (policy.BanPreciseNumbers)
			{
				if (numeric.Any(t => text.Contains(t))) return false;
			}
			return true;
		}
	}
}
