using System;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

namespace CityAI.AI.Utils
{
	public static class SelectionConfig
	{
		public static bool EnableSelection = true; // 灰度开关
		public static float Temperature = 0.0f; // 确定性输出
		public static int MaxTokens = 256; // 限制输出长度
		
		// ⭐ 智能重试策略（性能优化）
		public static bool EnableSmartRetry = true; // 启用智能重试（遇到余额不足时自动换号重试）
		public static int MaxRetryCount = 3; // 最大重试次数
		public static int RequestDelaySeconds = 1; // 请求间隔（秒），避免过于频繁
		
		// 重新登录成功事件（用于通知UI更新积分）
		public static event Action<int> OnReloginSuccess;
		
		/// <summary>
		/// 触发重新登录成功事件
		/// </summary>
		public static void NotifyReloginSuccess(int newPoints)
		{
			OnReloginSuccess?.Invoke(newPoints);
		}
	}

	public static class SmallLLM
	{
		/// <summary>
		/// 运行小模型选择（Top2/TopK）
		/// 智能重试版：只在遇到余额不足时才换号重试（性能优化）
		/// </summary>
		public static async Task<TPick> RunSelectionAsync<TPick>(string systemPrompt, object selectionInput, string schema = null, int maxRetries = 0)
		{
			// ⭐ 使用智能重试配置
			int actualMaxRetries = SelectionConfig.EnableSmartRetry ? SelectionConfig.MaxRetryCount : 1;
			
			for (int retry = 0; retry < actualMaxRetries; retry++)
			{
				try
				{
					// ⭐ 请求延迟（仅重试时需要）
					if (SelectionConfig.RequestDelaySeconds > 0 && retry > 0)
					{
						await RequestThrottler.WaitIfNeeded(SelectionConfig.RequestDelaySeconds);
					}
					
					// 1. 序列化输入
					var inputJson = JsonConvert.SerializeObject(selectionInput, Formatting.Indented);
					SelectionLogger.Log("SmallLLM_Input", inputJson);
					
				// 2. 调用 AI API（复用现有 AIManager）
				var aiManager = CityAI.AI.Core.AIManager.Instance;
				if (aiManager?.openai == null)
				{
					Debug.LogWarning("[SmallLLM] AI API 未初始化，返回默认值");
					return default(TPick);
				}
				
				// ⭐ 获取配置的模型名称（使用qwen-turbo而不是qwen-plus）
				var config = aiManager.GetConfig();
				string modelName = config != null ? config.chatModel : "qwen-turbo";
				// Debug.Log($"[SmallLLM] 使用模型: {modelName}");
				
				// 3. 创建专用会话（低温度确保确定性）
				var session = new CityAI.AI.Core.ChatSession(aiManager.openai, systemPrompt, modelName);
				session.Temperature = SelectionConfig.Temperature;
				session.MaxTokens = SelectionConfig.MaxTokens;
					
					// 4. 发送请求并获取响应
					var responseJson = await session.SendAsync(inputJson);
					
					// 如果返回空，可能是限流或其他错误，重试
					if (string.IsNullOrEmpty(responseJson))
					{
						if (retry < maxRetries - 1)
						{
							int waitSeconds = (retry + 1) * 2;
							Debug.LogWarning($"[SmallLLM] 第{retry+1}次尝试返回空响应，等待{waitSeconds}秒后重试...");
							await Task.Delay(waitSeconds * 1000);
							continue;
						}
						else
						{
							Debug.LogError($"[SmallLLM] 重试{maxRetries}次后仍返回空响应");
							return default(TPick);
						}
					}
					
					SelectionLogger.Log("SmallLLM_Output", responseJson);
					
					// 5. Schema 校验（如果提供）
					if (!string.IsNullOrEmpty(schema))
					{
						var isValid = JsonSchemaValidator.Validate(responseJson, schema);
						if (!isValid)
						{
							if (retry < maxRetries - 1)
							{
								Debug.LogWarning($"[SmallLLM] Schema 校验失败，尝试重试...");
								await Task.Delay(1000);
								continue;
							}
							else
							{
								Debug.LogWarning($"[SmallLLM] Schema 校验失败，返回默认值");
								return default(TPick);
							}
						}
					}
					
					// 6. 反序列化为 Pick DTO
					var pick = JsonConvert.DeserializeObject<TPick>(responseJson);
					
					if (pick == null)
					{
						if (retry < maxRetries - 1)
						{
							Debug.LogWarning($"[SmallLLM] 反序列化失败，尝试重试...");
							await Task.Delay(1000);
							continue;
						}
						else
						{
							Debug.LogWarning($"[SmallLLM] 反序列化失败，返回默认值");
							return default(TPick);
						}
					}
					
					// Debug.Log($"[SmallLLM] 选择成功：{typeof(TPick).Name}");
					return pick;
				}
				catch (Exception e)
				{
					string errorMsg = e.Message;
					bool isInsufficientBalance = errorMsg.Contains("余额不足") || errorMsg.Contains("insufficient") || errorMsg.Contains("Insufficient");
					
					Debug.LogWarning($"[SmallLLM] 尝试 {retry + 1}/{actualMaxRetries} 失败：{errorMsg}");
					
					// ⭐ 智能重试：只有遇到余额不足且还有重试次数时，才重新登录换号
					if (isInsufficientBalance && SelectionConfig.EnableSmartRetry && retry < actualMaxRetries - 1)
					{
						// Debug.Log($"[SmallLLM] 💡 检测到余额不足，尝试换新账户重试（剩余重试次数：{actualMaxRetries - retry - 1}）");
						bool reloginSuccess = await ForceReloginWithNewId();
						
						if (!reloginSuccess)
						{
							Debug.LogWarning($"[SmallLLM] ⚠️ 换号失败，将使用当前账户重试");
						}
					}
					else if (retry < actualMaxRetries - 1)
					{
						int waitSeconds = (retry + 1);
						// Debug.Log($"[SmallLLM] 等待{waitSeconds}秒后重试...");
						await Task.Delay(waitSeconds * 1000);
					}
					else
					{
						Debug.LogError($"[SmallLLM] ❌ 所有重试失败（共{actualMaxRetries}次），返回默认值");
					}
				}
			}
			
			return default(TPick);
		}
		
		/// <summary>
		/// 强制重新登录（换新账户）
		/// 用途：
		/// 1. 面板打开时换新账户
		/// 2. 遇到余额不足时换新账户重试
		/// </summary>
		public static async Task<bool> ForceReloginWithNewId()
		{
			try
			{
				var aiManager = CityAI.AI.Core.AIManager.Instance;
				if (aiManager == null)
				{
					Debug.LogWarning("[SmallLLM] AIManager 未找到，跳过重新登录");
					return false;
				}
				
				// 生成随机 openid（模拟不同设备）
				string randomOpenId = System.Guid.NewGuid().ToString();
				
				// Debug.Log($"[SmallLLM] 🔄 换新账户登录，新OpenID: {randomOpenId.Substring(0, 8)}...");
				
				// ⭐ 使用 ForceLoginAsync（强制重新登录，不受已登录状态限制）
				bool success = await aiManager.ForceLoginAsync(randomOpenId, "SmallLLM");
				
				if (success)
				{
					var userInfo = aiManager.GetUserInfo();
					int points = userInfo != null ? userInfo.point : 0;
					// Debug.Log($"[SmallLLM] ✅ 换号成功，新账户积分: {points}");
					
					// ⭐ 通知UI更新积分
					SelectionConfig.NotifyReloginSuccess(points);
				}
				else
				{
					Debug.LogWarning($"[SmallLLM] ⚠️ 换号失败");
				}
				
				return success;
			}
			catch (Exception e)
			{
				Debug.LogError($"[SmallLLM] 重新登录异常: {e.Message}");
				return false;
			}
		}
	}
	
	/// <summary>
	/// 请求节流器（防止IP限流）
	/// </summary>
	public static class RequestThrottler
	{
		private static DateTime lastRequestTime = DateTime.MinValue;
		
		public static async Task WaitIfNeeded(int minIntervalSeconds)
		{
			var minInterval = TimeSpan.FromSeconds(minIntervalSeconds);
			var elapsed = DateTime.Now - lastRequestTime;
			
			if (elapsed < minInterval)
			{
				var waitTime = minInterval - elapsed;
				// Debug.Log($"[RequestThrottler] ⏰ 等待 {waitTime.TotalSeconds:F1}秒 避免IP限流...");
				await Task.Delay(waitTime);
			}
			
			lastRequestTime = DateTime.Now;
		}
	}
}
