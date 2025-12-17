using System;
using System.Collections.Generic;

namespace CityAI.StockMarket.Model
{
	[Serializable]
	public class Holding
	{
		public string StockId;
		public int Quantity;
		public int AvgCostCents;
	}

	[Serializable]
	public class Trade
	{
		public int TimeIndex;
		public string StockId;
		public bool IsBuy;
		public int Quantity;
		public int PriceCents;
		public int CashChangeCents; // negative for buy, positive for sell
	}

	/// <summary>
	/// 投资组合管理（仅管理股票持仓，现金由 PlayerModel 统一管理）
	/// </summary>
	[Serializable]
	public class Portfolio
	{
		public Dictionary<string, Holding> Holdings = new Dictionary<string, Holding>();
		public List<Trade> History = new List<Trade>();

		public Portfolio()
		{
		}

		/// <summary>
		/// 购买股票（不管理现金，只记录持仓）
		/// </summary>
		public void Buy(string stockId, int quantity, int priceCents, int timeIndex)
		{
			if (quantity <= 0) return;
			
			long cost = (long)quantity * priceCents;
			
			if (!Holdings.TryGetValue(stockId, out var h))
			{
				h = new Holding { StockId = stockId, Quantity = 0, AvgCostCents = 0 };
				Holdings[stockId] = h;
			}
			
			int totalShares = h.Quantity + quantity;
			int totalCostCents = h.AvgCostCents * h.Quantity + priceCents * quantity;
			h.Quantity = totalShares;
			h.AvgCostCents = totalShares > 0 ? totalCostCents / totalShares : 0;
			
			History.Add(new Trade 
			{ 
				TimeIndex = timeIndex, 
				StockId = stockId, 
				IsBuy = true, 
				Quantity = quantity, 
				PriceCents = priceCents, 
				CashChangeCents = -(int)cost 
			});
		}

		/// <summary>
		/// 卖出股票（不管理现金，只记录持仓）
		/// </summary>
		public bool TrySell(string stockId, int quantity, int priceCents, int timeIndex)
		{
			if (quantity <= 0) return false;
			if (!Holdings.TryGetValue(stockId, out var h)) return false;
			if (quantity > h.Quantity) return false;
			
			h.Quantity -= quantity;
			int proceeds = quantity * priceCents;
			
			History.Add(new Trade 
			{ 
				TimeIndex = timeIndex, 
				StockId = stockId, 
				IsBuy = false, 
				Quantity = quantity, 
				PriceCents = priceCents, 
				CashChangeCents = proceeds 
			});
			
			return true;
		}
	}
}


