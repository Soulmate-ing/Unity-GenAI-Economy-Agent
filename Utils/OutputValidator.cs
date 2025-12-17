using System;
using System.Linq;
using CityAI.AI.Config;

namespace CityAI.AI.Utils
{
	public static class OutputValidator
	{
		private static readonly string[] DefaultForbiddenTokens =
		{
			"元","块","%","百分之","点","收益","利润","概率","几率","目标价","止损价","买入价","卖出价"
		};

		public static bool Validate(string text, string systemId)
		{
			if (string.IsNullOrWhiteSpace(text)) return false;
            var cfg = AIValidationConfig.Get();
            int maxLines = cfg != null ? cfg.maxLines : 4;
            int maxLen = cfg != null ? cfg.maxLineLength : 24;
			var lines = text.Split(new[] {'\n','\r'}, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length == 0 || lines.Length > maxLines) return false;
			if (lines.Any(l => l.Trim().Length > maxLen)) return false;

            var tokens = cfg != null && cfg.forbiddenTokens != null && cfg.forbiddenTokens.Length > 0
                ? cfg.forbiddenTokens : DefaultForbiddenTokens;
			if (tokens.Any(t => text.Contains(t))) return false;

			// 模板/口径：股票需出现风向/观望等词；彩票需出现锦鲤/谨慎等词之一
			if (systemId == "Stocks")
			{
                var keywords = cfg != null && cfg.stockDirectionalWords != null && cfg.stockDirectionalWords.Length > 0
                    ? cfg.stockDirectionalWords : new[] {"风向", "上涨", "下跌", "观望", "转弱", "偏暖", "谨慎"};
				if (!keywords.Any(k => text.Contains(k))) return false;
			}
			else if (systemId == "Lottery")
			{
                var keywords = cfg != null && cfg.lotteryKeywords != null && cfg.lotteryKeywords.Length > 0
                    ? cfg.lotteryKeywords : new[] {"锦鲤", "谨慎", "今日", "参与"};
				if (!keywords.Any(k => text.Contains(k))) return false;
			}
			return true;
		}
	}
}
