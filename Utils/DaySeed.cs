using System;
using System.Security.Cryptography;
using System.Text;

namespace CityAI.AI.Utils
{
	public static class DaySeed
	{
		public static string Compute(string yyyymmdd, string serverSalt, string scope)
		{
			using var sha = SHA256.Create();
			var bytes = Encoding.UTF8.GetBytes($"{yyyymmdd}:{serverSalt}:{scope}");
			var hash = sha.ComputeHash(bytes);
			var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
			return hex.Substring(0, 16);
		}
	}

	public static class SelectionLogger
	{
		public static void Log(string tag, string payload)
		{
			// 轻量占位：后续可改为落盘到 Application.persistentDataPath
			UnityEngine.Debug.Log($"[Selection] {tag}: {payload}");
		}
	}
}


