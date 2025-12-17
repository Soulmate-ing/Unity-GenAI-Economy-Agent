using System.Linq;

namespace CityAI.AI.Utils
{
	/// <summary>
	/// 查询意图辅助：统一的比喻/闲聊判定与关键词工具
	/// </summary>
	public static class QueryIntentHelper
	{
		private static readonly string[] MetaphoricalPatterns =
		{
			"我是股票", "我是彩票", "我是股票本人", "我是彩票本人",
			"股票是我", "彩票是我", "股票本人", "彩票本人",
			"我变成股票", "我变成彩票", "股票变成我", "彩票变成我"
		};

		private static readonly string[] CasualPatterns =
		{
			"股票是什么", "彩票是什么", "什么是股票", "什么是彩票",
			"股票好玩吗", "彩票好玩吗", "股票有意思吗", "彩票有意思吗",
			"股票长什么样", "彩票长什么样", "股票像什么", "彩票像什么"
		};

		public static bool IsMetaphoricalOrCasual(string questionLower)
		{
			if (string.IsNullOrEmpty(questionLower)) return false;
			return MetaphoricalPatterns.Any(questionLower.Contains) || CasualPatterns.Any(questionLower.Contains);
		}

		public static bool ContainsAny(string questionLower, string[] keywords)
		{
			if (string.IsNullOrEmpty(questionLower) || keywords == null || keywords.Length == 0) return false;
			return keywords.Any(questionLower.Contains);
		}

		// 股票/彩票关键词集合
		public static readonly string[] StockInvestmentKeywords =
		{
			"买什么股票","什么股票","股票推荐","买股票","投资股票","股票投资","股票分析","股票建议"
		};
		public static readonly string[] StockGeneralKeywords =
		{
			"股票","涨跌","买入","卖出","投资","股市","股价","涨停","跌停","板块","推荐"
		};
		public static readonly string[] LotteryInvestmentKeywords =
		{
			"买什么彩票","什么彩票","彩票推荐","买彩票","彩票购买","彩票中奖","彩票分析","彩票建议"
		};
		public static readonly string[] LotteryGeneralKeywords =
		{
			"彩票","中奖","购买","锦鲤","刮奖","抽奖","运气","推荐"
		};

		public static bool HasStockInvestmentIntent(string questionLower) => ContainsAny(questionLower, StockInvestmentKeywords);
		public static bool HasStockGeneralKeywords(string questionLower) => ContainsAny(questionLower, StockGeneralKeywords);
		public static bool HasLotteryInvestmentIntent(string questionLower) => ContainsAny(questionLower, LotteryInvestmentKeywords);
		public static bool HasLotteryGeneralKeywords(string questionLower) => ContainsAny(questionLower, LotteryGeneralKeywords);

		public static bool IsStockIntent(string questionLower)
		{
			if (string.IsNullOrEmpty(questionLower)) return false;
			if (IsMetaphoricalOrCasual(questionLower)) return false;
			return HasStockInvestmentIntent(questionLower) || HasStockGeneralKeywords(questionLower);
		}

		public static bool IsLotteryIntent(string questionLower)
		{
			if (string.IsNullOrEmpty(questionLower)) return false;
			if (IsMetaphoricalOrCasual(questionLower)) return false;
			return HasLotteryInvestmentIntent(questionLower) || HasLotteryGeneralKeywords(questionLower);
		}
	}
}
