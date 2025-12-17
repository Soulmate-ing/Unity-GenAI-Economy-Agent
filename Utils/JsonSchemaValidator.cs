using System;
using UnityEngine;

namespace CityAI.AI.Utils
{
	/// <summary>
	/// JSON Schema 校验器（轻量实现）
	/// 未来可升级为 Newtonsoft.Json.Schema 或 NJsonSchema
	/// </summary>
	public static class JsonSchemaValidator
	{
		/// <summary>
		/// 验证 JSON 是否符合 Schema（当前为基础校验）
		/// </summary>
		public static bool Validate(string json, string schemaJson)
		{
			if (string.IsNullOrWhiteSpace(json)) 
			{
				Debug.LogWarning("[JsonSchemaValidator] JSON 为空");
				return false;
			}

			try
			{
				// 基础校验：检查是否为有效 JSON
				var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
				
				// TODO: 未来接入完整 Schema 校验库
				// var schema = JSchema.Parse(schemaJson);
				// return obj.IsValid(schema);
				
				// 当前简化版：只检查必需字段存在
				if (!string.IsNullOrEmpty(schemaJson))
				{
					return ValidateBasicStructure(obj, schemaJson);
				}
				
				return true;
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[JsonSchemaValidator] JSON 解析失败: {e.Message}");
				return false;
			}
		}
		
		/// <summary>
		/// 基础结构校验（检查必需字段）
		/// </summary>
		private static bool ValidateBasicStructure(Newtonsoft.Json.Linq.JObject obj, string schemaType)
		{
			switch (schemaType)
			{
				case "LotteryPick":
					return obj.ContainsKey("picks") && obj.ContainsKey("notes");
				
				case "StockPick":
					return obj.ContainsKey("picks") && obj.ContainsKey("title");
				
				case "ResellPick":
					// ⭐ 移除 route 字段要求（已不再需要路线规划）
					return obj.ContainsKey("picks") && obj.ContainsKey("title");
				
				default:
					// 未指定 Schema 类型，只要是有效 JSON 就通过
					return true;
			}
		}
	}
}

