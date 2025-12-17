using System;
using System.Collections.Generic;
using UnityEngine;
using CityAI.StockMarket.Model;
using CityAI.Common;
using CityAI.Common.Events;
using CityAI.Player.Model;
using System.Linq;

namespace CityAI.StockMarket.Ctrl
{
	public class StockMarketController : MonoBehaviour
	{
	[Header("Simulation")]
	public int Seed = 20240901;
	
	[Header("Time Management")]
	[Tooltip("股票系统现在完全由全局GameTimeManager驱动（不可更改）")]
	public bool UseGlobalTimeManager = true; // 固定为true，股票系统必须使用全局时间

	public MarketSession Session { get; private set; }
	public Portfolio Portfolio { get; private set; }
	
	private PlayerModel _playerModel;
	private bool _subscribedToTimeManager = false;
	private Dictionary<string, System.Random> _rngMap; // 保存每个股票的RNG以支持无限演化

	private string FilePath = "Assets/SaveData/StockMarket/Data/LLMKnowledge{0}.json";

	private void Start()
	{
		// 订阅全局时间管理器
		if (GameTimeManager.Instance != null)
		{
			GameTimeManager.Instance.OnHourAdvanced += OnGameTimeAdvanced;
			_subscribedToTimeManager = true;
			
			Debug.Log("[StockMarketController] 已连接到GameTimeManager，股票价格将随游戏时间更新");
		}
		else
		{
			Debug.LogError("[StockMarketController] 未找到GameTimeManager！股票系统需要全局时间管理器才能运行");
		}
		
		// 自动初始化：尝试自动获取PlayerModel
		AutoInitialize();
	}
	
	/// <summary>
	/// 自动初始化股票系统（无需外部调用）
	/// </summary>
	private void AutoInitialize()
	{
		if (Session != null)
		{
			Debug.Log("[StockMarketController] 已经初始化过了");
			return;
		}
		
		// 尝试找到PlayerModel
		var playerController = FindObjectOfType<CityAI.Player.Ctrl.PlayerController>();
		if (playerController != null && playerController.Model != null)
		{
			Initialize(playerController.Model);
			Debug.Log("[StockMarketController] ✅ 自动初始化完成");
		}
		else
		{
			Debug.LogWarning("[StockMarketController] ⚠️ 未找到PlayerModel，延迟初始化。请确保场景中有PlayerController。");
		}
	}
	
	private void OnDestroy()
	{
		// 取消订阅
		if (_subscribedToTimeManager && GameTimeManager.Instance != null)
		{
			GameTimeManager.Instance.OnHourAdvanced -= OnGameTimeAdvanced;
		}
	}
	
	private void OnGameTimeAdvanced(int totalHours)
	{
		// 由GameTimeManager驱动时间前进
		if (Session != null)
		{
			// 如果需要，动态扩展价格序列（支持无限演化）
			int previousCount = Session.Stocks?.FirstOrDefault()?.PriceSeriesCents?.Count ?? 0;
			ExtendPriceSeriesIfNeeded(totalHours);
			int newCount = Session.Stocks?.FirstOrDefault()?.PriceSeriesCents?.Count ?? 0;
			
			// 诊断：检查价格是否真的更新了
			if (newCount > previousCount)
			{
				// 价格序列已扩展，记录价格变化
				foreach (var stock in Session.Stocks)
				{
					if (stock.PriceSeriesCents.Count > totalHours && totalHours > 0)
					{
						int prevPrice = stock.PriceSeriesCents[totalHours - 1];
						int currentPrice = stock.PriceSeriesCents[totalHours];
						if (prevPrice == currentPrice)
						{
							// 价格没有变化，记录警告
							Debug.LogWarning($"[StockMarket] 股票 {stock.Id} 在第{totalHours}小时价格未变化: {prevPrice/100m:F2}元 (特性={stock.Profile}, 波动率={stock.RtLow:F3}-{stock.RtHigh:F3})");
						}
					}
				}
			}
			else if (newCount == previousCount && totalHours >= previousCount)
			{
				Debug.LogWarning($"[StockMarket] 价格序列未扩展: 当前小时={totalHours}, 序列长度={previousCount}");
			}
			
			Session.CurrentHourIndex = totalHours;
			
			// 股票价格变化时，更新玩家总资产（现金 + 股票市值）
			UpdateTotalAssets();
		}
	}

	/// <summary>
	/// 动态扩展价格序列（支持无限演化）
	/// </summary>
	/// <param name="targetHour">目标小时索引</param>
	private void ExtendPriceSeriesIfNeeded(int targetHour)
	{
		if (Session == null || _rngMap == null) return;
		
		foreach (var stock in Session.Stocks)
		{
			while (stock.PriceSeriesCents.Count <= targetHour)
			{
				int currentHour = stock.PriceSeriesCents.Count - 1;
				int dayIndex = (currentHour / MarketConfig.HoursPerDay) + 1;
				
				// 获取当天的sector效果
				var dayEffects = Session.GetEffectsForDay(dayIndex);
				var rng = _rngMap[stock.Id];
				
				// 使用正态分布采样
				decimal mean = (stock.RtLow + stock.RtHigh) / 2m;
				decimal stdDev = (stock.RtHigh - stock.RtLow) / 6m;
				var rt = SampleNormalDecimal(rng, mean, stdDev);
				rt = Math.Max(stock.RtLow, Math.Min(stock.RtHigh, rt));
				
				// 25%概率出现异常波动（进一步增加活跃度）
				if (rng.NextDouble() < 0.25)
				{
					decimal spike = (decimal)(rng.NextDouble() * 0.30 - 0.15); // -15%~+15%
					rt = Math.Max(0.75m, Math.Min(1.30m, rt + spike));
				}
				
				decimal sd = 0m;
				if (MarketConfig.SectorEffectApplication == SectorEffectMode.Hourly)
				{
					sd = ComputeDailySectorSum(stock, dayEffects);
				}
				
				int currentPrice = stock.PriceSeriesCents[currentHour];
				int nextPrice;
				
				// 诊断：记录计算前的状态
				bool isDayBoundary = false;
				decimal originalFactor = rt * (1m + sd);
				
				if (MarketConfig.SectorEffectApplication == SectorEffectMode.Hourly)
				{
					nextPrice = PriceMath.ApplyHourlyUpdate(currentPrice, rt, sd);
				}
				else
				{
					nextPrice = PriceMath.ApplyHourlyUpdate(currentPrice, rt, 0m);
					// 日边界处理
					int nextHour = currentHour + 1;
					int nextDayIndex = (nextHour / MarketConfig.HoursPerDay) + 1;
					if (nextDayIndex != dayIndex)
					{
						isDayBoundary = true;
						var daySum = ComputeDailySectorSum(stock, dayEffects);
						var yuan = nextPrice / 100m;
						yuan *= (1m + daySum);
						nextPrice = MarketConfig.PriceToCents(yuan);
					}
				}
				
				int priceBeforeClamp = nextPrice;
				nextPrice = ClampToBand(stock, nextPrice);
				
				// 诊断：如果价格没有变化，记录详细信息
				if (nextPrice == currentPrice && currentHour > 0)
				{
					Debug.LogWarning($"[StockMarket] 价格未变化: 股票={stock.Id}, 小时={currentHour}, 价格={currentPrice/100m:F2}元, " +
						$"因子={originalFactor:F6}, 模式={(MarketConfig.SectorEffectApplication == SectorEffectMode.Hourly ? "Hourly" : "DailyOnce")}, " +
						$"日边界={isDayBoundary}, 计算后={priceBeforeClamp/100m:F2}元, 限制后={nextPrice/100m:F2}元, " +
						$"特性={stock.Profile}, 波动率={stock.RtLow:F3}-{stock.RtHigh:F3}, " +
						$"价格带={stock.LowerBandCents/100m:F2}-{stock.UpperBandCents/100m:F2}元");
				}
				
				stock.PriceSeriesCents.Add(nextPrice);
			}
		}
	}
	
	/// <summary>
	/// 初始化股票系统
	/// </summary>
	/// <param name="playerModel">玩家模型，用于管理现金</param>
	public void Initialize(PlayerModel playerModel)
	{
		_playerModel = playerModel ?? throw new ArgumentNullException(nameof(playerModel));
		
		var candidates = StockLibrary.GenerateCandidates(Seed);
		var sessionStocks = StockLibrary.PickSessionStocks(candidates, MarketConfig.SessionStockCount, Seed + 1);
		var session = new MarketSession { Seed = Seed, Stocks = sessionStocks };
		session.DailyEffects = DailyEventGenerator.GenerateDailyEffects(Seed + 2);
		GeneratePriceSeries(session);
		Session = session;
		Portfolio = new Portfolio();

		StockMarketDTO.ExportDailyEffectsAndTagsToJson(session, FilePath);
		
		Debug.Log($"[StockMarketController] 初始化完成 - 种子: {Seed}, 股票数: {Session.Stocks.Count}, 玩家现金: ¥{_playerModel.CashYuan:F2}");
		
		// 🔥 发布系统初始化事件
		EventBus.Instance?.Publish(new SystemInitializedEvent
		{
			SystemType = "Stock",
			InitializationStatus = true,
			Source = "StockMarketController"
		});
	}

	// 股票系统的时间完全由GameTimeManager的OnHourAdvanced事件驱动
	// 不再需要Update方法和AdvanceHour方法

		public int GetCurrentPriceCents(string stockId)
		{
			var s = Session.Stocks.Find(x => x.Id == stockId);
			if (s == null) return 0;
			int idx = Mathf.Clamp(Session.CurrentHourIndex, 0, s.PriceSeriesCents.Count - 1);
			return s.PriceSeriesCents[idx];
		}

		/// <summary>
		/// 购买股票
		/// </summary>
		public bool Buy(string stockId, int quantity)
		{
			if (_playerModel == null)
			{
				Debug.LogError("[StockMarket] PlayerModel not initialized. Call Initialize(PlayerModel) first.");
				return false;
			}
			
			var price = GetCurrentPriceCents(stockId);
			long cost = (long)quantity * price;
			
			// 检查现金是否足够
			if (_playerModel.CashCents < cost)
			{
				Debug.LogWarning($"[StockMarket] 现金不足: 需要 ¥{cost / 100m:F2}, 拥有 ¥{_playerModel.CashCents / 100m:F2}");
				return false;
			}
			
			// 扣除现金（只减现金，不减总资产，因为资产转为股票）
			_playerModel.CashCents -= (int)cost;
			
			// 记录持仓
			Portfolio.Buy(stockId, quantity, price, Session.CurrentHourIndex);
			
			// 更新总资产 = 现金 + 股票市值
			UpdateTotalAssets();
			
			Debug.Log($"[StockMarket] 买入 {stockId} x{quantity} @ ¥{price / 100m:F2}, 总花费: ¥{cost / 100m:F2}");
			
			// 🔥 发布事件总线事件
			EventBus.Instance?.Publish(new StockPurchasedEvent 
			{ 
				StockId = stockId,
				Quantity = quantity,
				PriceCents = price,
				TotalCost = cost,
				Source = "StockMarketController"
			});
			
			return true;
		}

		/// <summary>
		/// 卖出股票
		/// </summary>
		public bool Sell(string stockId, int quantity)
		{
			if (_playerModel == null)
			{
				Debug.LogError("[StockMarket] PlayerModel not initialized. Call Initialize(PlayerModel) first.");
				return false;
			}
			
			var price = GetCurrentPriceCents(stockId);
			
			// 尝试卖出（Portfolio会检查持仓）
			if (!Portfolio.TrySell(stockId, quantity, price, Session.CurrentHourIndex))
			{
				Debug.LogWarning($"[StockMarket] 卖出失败: 持仓不足或无效操作");
				return false;
			}
			
			// 增加现金（只加现金，不加总资产，因为股票转为现金）
			int proceeds = quantity * price;
			_playerModel.CashCents += proceeds;
			
			// 更新总资产 = 现金 + 股票市值
			UpdateTotalAssets();
			
			Debug.Log($"[StockMarket] 卖出 {stockId} x{quantity} @ ¥{price / 100m:F2}, 获得: ¥{proceeds / 100m:F2}");
			
			// 🔥 发布事件总线事件
			EventBus.Instance?.Publish(new StockSoldEvent 
			{ 
				StockId = stockId,
				Quantity = quantity,
				PriceCents = price,
				TotalProceeds = proceeds,
				Source = "StockMarketController"
			});
			
			return true;
		}
		
		/// <summary>
		/// 计算股票组合的当前市值
		/// </summary>
		public long GetStockPortfolioValue()
		{
			long totalValue = 0;
			foreach (var holding in Portfolio.Holdings.Values)
			{
				int currentPrice = GetCurrentPriceCents(holding.StockId);
				totalValue += (long)holding.Quantity * currentPrice;
			}
			return totalValue;
		}
		
		/// <summary>
		/// 更新玩家总资产（现金 + 股票市值）
		/// </summary>
		private void UpdateTotalAssets()
		{
			if (_playerModel == null) return;
			
			long stockValue = GetStockPortfolioValue();
			_playerModel.TotalAssetsCents = _playerModel.CashCents + stockValue;
			
			Debug.Log($"[StockMarket] 总资产更新: 现金 ¥{_playerModel.CashCents / 100m:F2} + 股票 ¥{stockValue / 100m:F2} = ¥{_playerModel.TotalAssetsCents / 100m:F2}");
			
			// 🔥 发布事件总线事件
			EventBus.Instance?.Publish(new StockPortfolioValueChangedEvent 
			{ 
				TotalValue = stockValue,
				Source = "StockMarketController"
			});
		}

	private void GeneratePriceSeries(MarketSession session)
	{
		// 初始化RNG映射（用于支持无限演化）
		_rngMap = new Dictionary<string, System.Random>();
		foreach (var s in session.Stocks)
		{
			var rng = new System.Random(HashCombine(session.Seed, s.Id.GetHashCode()));
			_rngMap[s.Id] = rng;
			// Initial price at t=0
			s.PriceSeriesCents.Clear();
			int initialPrice = Math.Max(s.InitialPriceCents, MarketConfig.MinPriceCents);
			s.PriceSeriesCents.Add(initialPrice);
			// 设置价格带（基于全局配置的倍数范围，限制极端价格）
			s.LowerBandCents = BandMath.ComputeLowerBand(initialPrice);
			s.UpperBandCents = BandMath.ComputeUpperBand(initialPrice);
		}

		for (int t = 0; t < MarketConfig.TotalHours; t++)
		{
			int dayIndex = (t / MarketConfig.HoursPerDay) + 1;
			var dayEffects = session.GetEffectsForDay(dayIndex);
			foreach (var s in session.Stocks)
			{
				var rng = _rngMap[s.Id];
				
				// 🔥 改进1：使用正态分布采样（更接近真实市场）
				decimal mean = (s.RtLow + s.RtHigh) / 2m;
				decimal stdDev = (s.RtHigh - s.RtLow) / 6m; // 3σ 原则
				var rt = SampleNormalDecimal(rng, mean, stdDev);
				rt = Math.Max(s.RtLow, Math.Min(s.RtHigh, rt)); // 限制在原始范围内
				
			// 🔥 改进2：25%概率出现异常波动（提高概率，增加市场活跃度）
			if (rng.NextDouble() < 0.25)
			{
				decimal spike = (decimal)(rng.NextDouble() * 0.30 - 0.15); // -15% ~ +15%（扩大范围）
				rt = Math.Max(0.75m, Math.Min(1.30m, rt + spike));
			}
				
				decimal sd = 0m;
				if (MarketConfig.SectorEffectApplication == SectorEffectMode.Hourly)
				{
					sd = ComputeDailySectorSum(s, dayEffects);
				}
				int current = s.PriceSeriesCents[t];
				int next;
				if (MarketConfig.SectorEffectApplication == SectorEffectMode.Hourly)
				{
					next = PriceMath.ApplyHourlyUpdate(current, rt, sd);
				}
				else
				{
					next = PriceMath.ApplyHourlyUpdate(current, rt, 0m);
					// Apply daily once at day boundary (when moving into hour 0 of next day)
					int nextHour = t + 1;
					int nextDayIndex = (nextHour / MarketConfig.HoursPerDay) + 1;
					if (nextDayIndex != dayIndex)
					{
						var daySum = ComputeDailySectorSum(s, dayEffects);
						var yuan = next / 100m;
						yuan *= (1m + daySum);
						next = MarketConfig.PriceToCents(yuan);
					}
				}
				next = ClampToBand(s, next);
				s.PriceSeriesCents.Add(next);
			}
		}
	}

		private static decimal ComputeDailySectorSum(Stock s, DailyTagEffects dayEffects)
		{
			decimal sum = 0m;
			foreach (var tag in s.Tags)
			{
				if (dayEffects.TagToEffect.TryGetValue(tag, out var e))
				{
					sum += e;
				}
			}
			sum = PriceMath.ClampSectorSum(sum);
			return sum;
		}

	private static decimal SampleUniformDecimal(System.Random rng, decimal min, decimal max)
	{
		var t = (decimal)rng.NextDouble();
		return min + (max - min) * t;
	}
	
	/// <summary>
	/// 正态分布采样（Box-Muller变换）
	/// 让价格波动更接近真实市场（大部分时间小幅波动，偶尔大幅波动）
	/// </summary>
	private static decimal SampleNormalDecimal(System.Random rng, decimal mean, decimal stdDev)
	{
		// Box-Muller 变换生成正态分布随机数
		double u1 = 1.0 - rng.NextDouble();
		double u2 = 1.0 - rng.NextDouble();
		double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
		return mean + stdDev * (decimal)randStdNormal;
	}

	private static int HashCombine(int a, int b)
	{
		unchecked
		{
			int hash = 17;
			hash = hash * 31 + a;
			hash = hash * 31 + b;
			return hash;
		}
	}

	private int ClampToBand(Stock stock, int price)
	{
		// 边界反弹：若越界，贴边并推动一个最小步长，避免长期贴边显示为0.00%
		if (price < stock.LowerBandCents)
		{
			return Math.Min(stock.LowerBandCents + 1, stock.UpperBandCents);
		}
		if (price > stock.UpperBandCents)
		{
			return Math.Max(stock.UpperBandCents - 1, stock.LowerBandCents);
		}
		return price;
	}
	}
}


