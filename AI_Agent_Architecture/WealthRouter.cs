using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using CityAI.Player.Model;
using CityAI.StockMarket.Ctrl;
using CityAI.Lottery.Ctrl;
using CityAI.AI.Handlers;
using CityAI.AI.Router.Models;
using CityAI.AI.Core;
using CityAI.AI.Utils;

namespace CityAI.AI.Router
{
    /// <summary>
    /// 财富路由器 - 统一管理多系统AI处理
    /// </summary>
    public class WealthRouter
    {
        private readonly StockMarketController stockController;
        private readonly LotteryController lotteryController;
        private readonly List<IAISystemHandler> handlers;
        private readonly IAISystemHandler defaultHandler;

        public WealthRouter(StockMarketController stockController, LotteryController lotteryController)
        {
            this.stockController = stockController;
            this.lotteryController = lotteryController;
            
            handlers = new List<IAISystemHandler>
            {
                new LotteryAIHandler(lotteryController),
                new StockAIHandler(stockController),
                new ResellAIHandler()  // ✅ 添加倒卖系统处理器
            };
            
            defaultHandler = new DefaultAIHandler();
        }

        /// <summary>
        /// 路由用户问题到合适的AI处理器
        /// </summary>
        public async Task<string> RouteAsync(string question, PlayerModel playerModel)
        {
            if (string.IsNullOrEmpty(question))
            {
                return "请提出你的疑问...";
            }

            // Debug.Log($"[WealthRouter] 处理问题：{question}");

            // 检测涉及的系统
            var systems = DetectSystems(question);
            // Debug.Log($"[WealthRouter] 检测到系统：{string.Join(", ", systems)}");

            if (systems.Count == 1)
            {
                // 单系统查询：直接调用对应处理器
                return await HandleSingleSystem(question, playerModel, systems[0]);
            }
            else if (systems.Count > 1)
            {
                // 多系统查询：并行处理 + 统一评分
                return await HandleMultipleSystems(question, playerModel);
            }
            else
            {
                // 无匹配系统：使用默认处理器
                // Debug.Log("[WealthRouter] 使用默认处理器");
                return await defaultHandler.HandleQueryAsync(question, playerModel);
            }
        }

        /// <summary>
        /// 检测问题涉及的系统
        /// </summary>
        private List<SystemId> DetectSystems(string question)
        {
            var systems = new List<SystemId>();
            var q = question.ToLowerInvariant();

            // ⭐ 先检测具体系统（如果明确提到）
            bool hasLottery = q.Contains("彩票") || q.Contains("刮刮") || q.Contains("面值") || q.Contains("第");
            bool hasStocks = q.Contains("股票") || q.Contains("板块") || q.Contains("做空") || q.Contains("风向") || q.Contains("观望");
            bool hasResell = q.Contains("倒卖") || q.Contains("热区") || q.Contains("摆摊") || q.Contains("进货") || q.Contains("路线") || q.Contains("批发") ||
                            q.Contains("卖什么") || q.Contains("什么商品") || q.Contains("商品") || q.Contains("什么好") || q.Contains("推荐商品") ||
                            q.Contains("卖货") || q.Contains("卖东西") || q.Contains("哪里卖") || q.Contains("去哪卖") || q.Contains("在哪卖") ||
                            q.Contains("倒买") || q.Contains("转手") || q.Contains("贩售");

            // ⭐ 如果明确提到了系统（如"卖货和彩票"），只计算提到的系统
            if (hasLottery || hasStocks || hasResell)
            {
                // 检查是否是"XX和XX"或"XX,XX"的并列询问
                bool isComparingSpecificSystems = (hasLottery && hasStocks) || (hasLottery && hasResell) || (hasStocks && hasResell) ||
                                                 q.Contains("和") || q.Contains(",");

                if (isComparingSpecificSystems)
                {
                    // 并列询问：只返回明确提到的系统
                    // Debug.Log("[WealthRouter] 检测到并列询问，只返回明确提到的系统");
                    if (hasLottery) systems.Add(SystemId.Lottery);
                    if (hasStocks) systems.Add(SystemId.Stocks);
                    if (hasResell) systems.Add(SystemId.Resell);
                    return systems.Count > 0 ? systems : new List<SystemId> { SystemId.Lottery, SystemId.Stocks, SystemId.Resell };
                }
                else
                {
                    // 单一系统询问：只返回该系统
                    if (hasLottery) systems.Add(SystemId.Lottery);
                    if (hasStocks) systems.Add(SystemId.Stocks);
                    if (hasResell) systems.Add(SystemId.Resell);
                    return systems;
                }
            }

            // ⭐ 如果没有明确提到系统，检测是否是"比较所有系统"的问题
            var isComparingAllSystems = 
                q.Contains("哪个系统") || q.Contains("哪个好") || q.Contains("选哪个") ||
                q.Contains("最快") || q.Contains("最赚钱") || q.Contains("最不赚钱") ||
                q.Contains("最便宜") || q.Contains("门槛最低") || q.Contains("练手") ||
                q.Contains("好拍") || q.Contains("容易火") || q.Contains("传播");

            if (isComparingAllSystems)
            {
                // 比较性问题：返回所有系统进行评分
                // Debug.Log("[WealthRouter] 检测到比较性问题，返回所有系统");
                return new List<SystemId> { SystemId.Lottery, SystemId.Stocks, SystemId.Resell };
            }

            // 默认：如果都没有匹配，返回空列表（将由默认处理器处理）
            return systems;
        }

        /// <summary>
        /// 处理单系统查询
        /// </summary>
        private async Task<string> HandleSingleSystem(string question, PlayerModel playerModel, SystemId systemId)
        {
            var handler = handlers.FirstOrDefault(h => GetSystemId(h) == systemId);
            if (handler != null)
            {
                // Debug.Log($"[WealthRouter] 单系统处理：{systemId}");
                return await handler.HandleQueryAsync(question, playerModel);
            }

            Debug.LogWarning($"[WealthRouter] 未找到处理器：{systemId}");
            return await defaultHandler.HandleQueryAsync(question, playerModel);
        }

        /// <summary>
        /// 处理多系统查询 - 使用 WealthScoring 评分并输出"主线+备选"结构化文本
        /// </summary>
        private async Task<string> HandleMultipleSystems(string question, PlayerModel playerModel)
        {
            // Debug.Log("[WealthRouter] 多系统并行处理（统一评分模式）");

            // 检查是否包含未来询问（统一工具）
            if (DateDetectionHelper.IsAskingAboutFuture(question))
            {
                // Debug.Log("[WealthRouter] 检测到未来询问，返回统一拒答");
                return ReplyHelper.GetFutureRejection();
            }

            var detectedSystems = DetectSystems(question);
            
            // 1. 提取目标偏好
            var objective = ExtractObjective(question);
            // Debug.Log($"[WealthRouter] 目标偏好：{objective}");

            // 2. 并行收集所有系统的 Snap 对象
            var snapTasks = new List<Task<Snap>>();

            foreach (var systemId in detectedSystems)
            {
                var handler = handlers.FirstOrDefault(h => GetSystemId(h) == systemId);
                if (handler != null)
                {
                    snapTasks.Add(TryGetSnapAsync(handler, question, playerModel));
                }
            }

            // 等待所有 Snap 生成完成
            var snaps = (await Task.WhenAll(snapTasks))
                .Where(s => s != null)
                .ToList();

            // Debug.Log($"[WealthRouter] 收集到 {snaps.Count} 个有效 Snap");

            if (snaps.Count == 0)
            {
                return "暂时无法提供建议，请稍后重试。";
            }

            // 3. 评分排序
            var ranked = snaps
                .OrderByDescending(s => Scoring.ScoreByObjective(s, playerModel.CashYuan, objective))
                .ToList();

            // Debug.Log($"[WealthRouter] 评分排序完成，排序结果：{string.Join(", ", ranked.Select(s => s.Id))}");

            // 4. 预算分配
            var allocations = AllocationPlanner.AllocateBudget(ranked, playerModel.CashYuan);
            Debug.Log($"[WealthRouter] 预算分配完成，分配方案数：{allocations.Count}");

            // 5. 渲染输出（根据问题类型决定是否显示备选）
            var showAlternative = IsAskingForComparison(question);
            return RenderMainAndAlternative(ranked, allocations, objective, showAlternative);
        }

        /// <summary>
        /// 尝试从 Handler 获取 Snap 对象
        /// </summary>
        private async Task<Snap> TryGetSnapAsync(IAISystemHandler handler, string question, PlayerModel player)
        {
            try
            {
                var snap = await handler.HandleQueryAsSnapAsync(question, player);
                if (snap != null)
                {
                    Debug.Log($"[WealthRouter] {handler.SystemId} Snap 生成成功");
                }
                else
                {
                    Debug.LogWarning($"[WealthRouter] {handler.SystemId} Snap 生成失败（返回null）");
                }
                return snap;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WealthRouter] {handler.SystemId} Handler 失败: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 检测是否是"并列比较"问题（如"彩票和股票"）
        /// 返回true表示应该显示多个系统（主线+备选），false表示只显示一个最优方案
        /// </summary>
        private bool IsAskingForComparison(string question)
        {
            var q = question.ToLowerInvariant();
            
            // 检测"XX和XX"模式（明确询问多个系统，应该显示多个）
            if ((q.Contains("彩票") && q.Contains("股票") && q.Contains("和")) ||
                (q.Contains("彩票") && q.Contains("倒卖") && q.Contains("和")) ||
                (q.Contains("股票") && q.Contains("倒卖") && q.Contains("和")) ||
                (q.Contains("彩票") && q.Contains("股票") && q.Contains(",")) ||
                (q.Contains("彩票") && q.Contains("倒卖") && q.Contains(",")) ||
                (q.Contains("股票") && q.Contains("倒卖") && q.Contains(",")))
            {
                return true;  // 明确并列询问，显示多个系统
            }
            
            // 检测"哪个最XX"、"最大化收益"等比较性问题（只输出一个最优方案）
            if (q.Contains("哪个最") || q.Contains("最大化") || q.Contains("最好") || 
                q.Contains("最优") || q.Contains("推荐") || q.Contains("应该"))
            {
                return false;  // 比较性问题，只显示一个最优方案
            }
            
            // 默认：只显示一个最优方案
            return false;
        }

        /// <summary>
        /// 渲染"主线+备选"格式文本
        /// </summary>
        private string RenderMainAndAlternative(List<Snap> ranked, List<Allocation> allocations, Objective objective, bool showAlternative = false)
        {
            var sb = new System.Text.StringBuilder();

            // 主线建议（排名第一的系统）
            if (ranked.Count > 0)
            {
                var main = ranked[0];
                var mainBudget = allocations.FirstOrDefault(a => a.Snap == main)?.Budget ?? 0;

                sb.AppendLine(main.Title);  // ⭐ 去掉"发财秘籍"前缀
                sb.AppendLine($"预算建议：¥{mainBudget:F0}");

                // 只有在有动作时才显示动作列表（彩票系统无动作，只有标题）
                if (main.Actions != null && main.Actions.Count > 0)
                {
                    sb.AppendLine();
                    foreach (var action in main.Actions)
                    {
                        var budgetHint = action.BudgetHint.HasValue ? $" ｜预算 ¥{action.BudgetHint.Value:F0}" : "";
                        sb.AppendLine($"  · {action.Detail}{budgetHint}");
                    }
                }
            }

            // 备选方案（仅在并列比较时显示）
            if (showAlternative && ranked.Count > 1)
            {
                sb.AppendLine();
                var alt = ranked[1];
                var altBudget = allocations.FirstOrDefault(a => a.Snap == alt)?.Budget ?? 0;

                sb.AppendLine($"【备选方案】{alt.Title}");
                sb.AppendLine($"预算建议：¥{altBudget:F0}");

                // 只有在有动作时才显示动作列表
                if (alt.Actions != null && alt.Actions.Count > 0)
                {
                    sb.AppendLine();
                    foreach (var action in alt.Actions.Take(2))
                    {
                        sb.AppendLine($"  · {action.Detail}");
                    }
                }
            }

            // 目标偏好说明
            sb.AppendLine();
            sb.AppendLine($"【目标偏好】{GetObjectiveText(objective)}");

            return sb.ToString();
        }

        /// <summary>
        /// 提取用户询问中的目标偏好
        /// </summary>
        private Objective ExtractObjective(string question)
        {
            var q = question.ToLowerInvariant();

            // 1️⃣ 先检查"最不赚钱"和"练手"（最高优先级，避免被"赚钱"误匹配）
            if (q.Contains("最不赚钱") || q.Contains("练手") || q.Contains("最差") || q.Contains("风险低") || q.Contains("不赚钱"))
            {
                return Objective.LeastProfit;
            }

            // 2️⃣ 再检查"赚钱"相关
            if (q.Contains("赚钱") || q.Contains("收益") || q.Contains("盈利") || q.Contains("赚得多"))
            {
                // 如果有"赚钱"，即使有"最快"也应该是 MaxProfit
                return Objective.MaxProfit;
            }

            // 3️⃣ 周转相关（明确是"周转"才算）
            if (q.Contains("周转") || q.Contains("回本") || q.Contains("变现"))
            {
                return Objective.FastTurnover;
            }

            // 4️⃣ 门槛相关
            if (q.Contains("门槛") || q.Contains("最便宜") || q.Contains("入门") || q.Contains("启动资金"))
            {
                return Objective.LowBarrier;
            }

            // 5️⃣ 传播相关
            if (q.Contains("好拍") || q.Contains("容易火") || q.Contains("传播") || q.Contains("病毒") || q.Contains("出圈"))
            {
                return Objective.ViralityFirst;
            }

            // 6️⃣ "最快"单独判断（放在最后，避免冲突）
            if (q.Contains("最快"))
            {
                // 如果只有"最快"，默认是 FastTurnover
                return Objective.FastTurnover;
            }

            // 7️⃣ 默认：最大化收益
            return Objective.MaxProfit;
        }

        /// <summary>
        /// 获取目标偏好的文本描述
        /// </summary>
        private string GetObjectiveText(Objective obj)
        {
            return obj switch
            {
                Objective.MaxProfit => "最大化收益",
                Objective.LeastProfit => "练手模式（最不赚钱）",
                Objective.FastTurnover => "快速周转",
                Objective.LowBarrier => "低门槛入场",
                Objective.ViralityFirst => "传播优先",
                _ => "综合评估"
            };
        }

        // 日期解析与未来判断改为使用 DateDetectionHelper/ReplyHelper，无需在本类重复实现

        /// <summary>
        /// 获取处理器的系统ID
        /// </summary>
        private SystemId GetSystemId(IAISystemHandler handler)
        {
            return handler.SystemId switch
            {
                "Lottery" => SystemId.Lottery,
                "Stocks" => SystemId.Stocks,
                "Resell" => SystemId.Resell,  // ✅ 添加倒卖系统
                _ => SystemId.Lottery // 默认
            };
        }
    }
}
