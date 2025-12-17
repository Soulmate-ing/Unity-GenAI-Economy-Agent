using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CityAI.AI.Router.Models;
using CityAI.AI.Utils;

namespace CityAI.AI.Router
{
	public static class SelectAndRender
	{
		public static async Task<Snap> LotteryTop2Async(string question, PlayerContext player, LotterySelectionInput input)
		{
			if (!SelectionConfig.EnableSelection)
			{
				// 选择链未开启：返回保守 Snap（不调用模型）
				return new Snap
				{
					Id = SystemId.Lottery,
					Title = "今日非锦鲤日，谨慎参与",
					Threshold = input.FaceValue,
					FundMin = input.FaceValue,
					FundMax = input.FaceValue * 20,
					Capacity = input.FaceValue,
					Turnover = 1.0,
					Edge = 0.0,
					Virality = 0.4,
					Actions = new List<ActionItem>()  // 彩票系统只需要标题提示是否锦鲤日，不需要具体动作
				};
			}
			var pick = await SmallLLM.RunSelectionAsync<LotteryPick>(SystemPrompts.LotterySelection, input, schema: "LotteryPick");
			return SnapFactory.FromLotteryPick(pick, input);
		}

	public static async Task<Snap> StockTop2Async(string question, PlayerContext player, StockSelectionInput input)
	{
		if (!SelectionConfig.EnableSelection)
		{
			// 兜底模式：基于简单规则生成标题和动作
			var (title, actions) = GenerateStockFallbackTitleAndActions(input);
			
			// 根据 buffs 计算 Edge
			var strongBuffs = input.Buffs.Where(b => b.Direction == "up" && b.Strength > 50).ToList();
			var weakBuffs = input.Buffs.Where(b => b.Direction == "down" && b.Strength > 50).ToList();
			var edge = 0.0;
			if (strongBuffs.Count > weakBuffs.Count)
				edge = 0.3;  // 强势板块多
			else if (weakBuffs.Count > strongBuffs.Count)
				edge = -0.3;  // 弱势板块多
			
			return new Snap
			{
				Id = SystemId.Stocks,
				Title = title,
				Threshold = 1000,
				FundMin = 500,        // 修改：降低下限，提高 fundFit
				FundMax = 20000,      // 修改：降低上限，提高 fundFit
				Capacity = 10000,     // 修改：降低容量，提高 deployable
				Turnover = 0.6,
				Edge = edge,          // 修改：根据 buffs 计算 Edge
				Virality = 0.3,
				Actions = actions
			};
		}
		var pick = await SmallLLM.RunSelectionAsync<StockPick>(SystemPrompts.StockSelection, input, schema: "StockPick");
		return SnapFactory.FromStockPick(pick, input);
	}
	
	/// <summary>
	/// 生成股票兜底标题和动作（基于简单规则，提及具体板块）
	/// </summary>
	private static (string title, List<ActionItem> actions) GenerateStockFallbackTitleAndActions(StockSelectionInput input)
	{
		// 1. 找出强势板块（direction=up 且 strength 高）
		var strongBuffs = input.Buffs
			.Where(b => b.Direction == "up" && b.Strength > 40)
			.OrderByDescending(b => b.Strength)
			.Take(2)
			.ToList();
		
		// 2. 找出弱势板块（direction=down 或 strength 低）
		var weakBuffs = input.Buffs
			.Where(b => b.Direction == "down" || (b.Direction == "up" && b.Strength < 30))
			.OrderBy(b => b.Strength)
			.Take(1)
			.ToList();
		
		var actions = new List<ActionItem>();
		string title;
		
		// 3. 生成标题和动作
		if (strongBuffs.Count > 0 && weakBuffs.Count > 0)
		{
			// 有强有弱：推荐强势，规避弱势
			var strong = strongBuffs[0].Sector;
			var weak = weakBuffs[0].Sector;
			title = $"{strong}偏强，{weak}转弱";
			
			actions.Add(new ActionItem 
			{ 
				Kind = ActionKind.Buy, 
				Detail = $"可关注{strong}板块机会" 
			});
			actions.Add(new ActionItem 
			{ 
				Kind = ActionKind.Wait, 
				Detail = $"规避{weak}板块风险" 
			});
		}
		else if (strongBuffs.Count >= 2)
		{
			// 多个强势：推荐强势板块
			var s1 = strongBuffs[0].Sector;
			var s2 = strongBuffs[1].Sector;
			title = $"{s1}、{s2}偏强";
			
			actions.Add(new ActionItem 
			{ 
				Kind = ActionKind.Buy, 
				Detail = $"可关注{s1}板块机会" 
			});
			actions.Add(new ActionItem 
			{ 
				Kind = ActionKind.Buy, 
				Detail = $"{s2}板块同步跟进" 
			});
		}
		else if (strongBuffs.Count > 0)
		{
			// 单个强势：推荐该板块，其他观望
			var strong = strongBuffs[0].Sector;
			title = $"{strong}偏强，观望其他";
			
			actions.Add(new ActionItem 
			{ 
				Kind = ActionKind.Buy, 
				Detail = $"可关注{strong}板块机会" 
			});
			actions.Add(new ActionItem 
			{ 
				Kind = ActionKind.Wait, 
				Detail = "其他板块持币观望" 
			});
		}
		else if (weakBuffs.Count > 0)
		{
			// 只有弱势：建议观望
			var weak = weakBuffs[0].Sector;
			title = $"{weak}转弱，谨慎参与";
			
			actions.Add(new ActionItem 
			{ 
				Kind = ActionKind.Wait, 
				Detail = $"规避{weak}板块风险" 
			});
			actions.Add(new ActionItem 
			{ 
				Kind = ActionKind.Wait, 
				Detail = "整体持币观望为主" 
			});
		}
		else
		{
			// 无数据：完全兜底
			title = "风向偏暖，谨慎参与";
			
			actions.Add(new ActionItem 
			{ 
				Kind = ActionKind.Wait, 
				Detail = "观望市场风向" 
			});
			actions.Add(new ActionItem 
			{ 
				Kind = ActionKind.Buy, 
				Detail = "轻仓试探强势板块" 
			});
		}
		
		return (title, actions);
	}

		public static async Task<Snap> ResellTop2Async(string question, PlayerContext player, ResellSelectionInput input)
		{
			if (!SelectionConfig.EnableSelection)
			{
				return new Snap
				{
					Id = SystemId.Resell,
					Title = "查看当前热区信息",
					Threshold = 100,
					FundMin = 100,
					FundMax = player.Fund,
					Capacity = input.Capacity,
					Turnover = 0.85,
					Edge = 0.0,
					Virality = 0.6,
					Actions = new List<ActionItem>
					{
						new ActionItem { Kind = ActionKind.Wait, Detail = "查看热区详情" }
					}
				};
			}
			var pick = await SmallLLM.RunSelectionAsync<ResellPick>(SystemPrompts.ResellSelection, input, schema: "ResellPick");
			
			// ⭐ 记录AI选择结果和原因（用于调试）
			if (pick != null && pick.Picks != null && pick.Picks.Count > 0)
			{
				UnityEngine.Debug.Log($"[SelectAndRender] AI选择了 {pick.Picks.Count} 个热区：{string.Join(", ", pick.Picks)}");
				
				// 记录每个选中热区的详细信息
				foreach (var pickedId in pick.Picks)
				{
					var selectedSurge = input.TodaySurges?.FirstOrDefault(s => s.Id == pickedId);
					if (selectedSurge != null)
					{
						UnityEngine.Debug.Log($"[SelectAndRender]   - {pickedId}: {selectedSurge.Zone} - {selectedSurge.ProductName}, 强度={selectedSurge.StrengthTag}, 剩余时间={selectedSurge.RemainingHours}小时");
					}
				}
			}
			
			return SnapFactory.FromResellPick(pick, input, player);
		}
	}
}


