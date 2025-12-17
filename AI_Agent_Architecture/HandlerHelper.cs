using System.Text;
using CityAI.AI.Router.Models;
using CityAI.Player.Model;

namespace CityAI.AI.Handlers
{
	/// <summary>
	/// Handler 辅助方法
	/// </summary>
	public static class HandlerHelper
	{
		/// <summary>
		/// PlayerModel → PlayerContext 转换
		/// </summary>
		public static PlayerContext ToPlayerContext(PlayerModel playerModel)
		{
			return new PlayerContext
			{
				Fund = playerModel.CashYuan,
				Zone = "", // TODO: 未来从玩家位置获取
				Capacity = 0 // TODO: 未来从载具获取
			};
		}
		
		/// <summary>
		/// Snap → 文本渲染（用于向后兼容）
		/// </summary>
		public static string RenderSnapToText(Snap snap)
	{
		var sb = new StringBuilder();
		
		// 标题
		sb.AppendLine(snap.Title);
		
		// ⭐ 倒卖系统只输出标题，不输出 Actions
		if (snap.Id != CityAI.AI.Router.Models.SystemId.Resell)
		{
			// 动作列表（其他系统正常输出）
			foreach (var action in snap.Actions)
			{
				sb.AppendLine($"- {action.Detail}");
			}
		}
		
		// 拒答（如果有）
		if (!string.IsNullOrEmpty(snap.Denial))
		{
			sb.AppendLine();
			sb.AppendLine(snap.Denial);
		}
		
		return sb.ToString().TrimEnd();
	}
		
		/// <summary>
		/// 生成当前日期种子
		/// </summary>
		public static string GenerateDaySeed(int currentDay, string scope)
		{
			// 使用统一的服务器盐值
			const string serverSalt = "CityAI_v1";
			var yyyymmdd = $"Day{currentDay:D4}";
			return CityAI.AI.Utils.DaySeed.Compute(yyyymmdd, serverSalt, scope);
		}
	}
}

