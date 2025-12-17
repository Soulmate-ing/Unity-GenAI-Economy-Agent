using CityAI.AI.Data;

namespace CityAI.AI.Utils
{
	/// <summary>
	/// 统一的拒答与话术获取工具
	/// 优先从 FortuneReplyManager 获取，缺省时使用内置兜底文案。
	/// </summary>
	public static class ReplyHelper
	{
		public static string GetFutureRejection()
		{
			var mgr = FortuneReplyManager.Instance;
			var s = mgr?.GetRandomReply("future");
			return string.IsNullOrEmpty(s) ? "天机不可泄露，未来的事未来再说" : s;
		}

		public static string GetPrecisePredictionRejection()
		{
			var mgr = FortuneReplyManager.Instance;
			var s = mgr?.GetRandomReply("precise_prediction");
			return string.IsNullOrEmpty(s) ? "只谈风向与时间窗，不给点位与收益" : s;
		}

		public static string GetIrrelevantReply()
		{
			var mgr = FortuneReplyManager.Instance;
			var s = mgr?.GetRandomReply("irrelevant");
			return string.IsNullOrEmpty(s) ? "抱歉，我只回答财富相关问题" : s;
		}
	}
}

