# Unity GenAI Economy Agent

> A Generative AI Economic Simulation Framework for Unity.
> 基于 LLM Agent 的生成式游戏经济系统仿真框架。

## 项目简介 (Introduction)

本项目展示了如何将 **Large Language Model (LLM)** 深度集成到模拟经营游戏中。
核心实现了一个 **Intent Router (意图路由)** 架构，能够解析玩家复杂的自然语言指令（如"帮我全仓买入科技股"），并驱动一个拟真的 **高斯分布经济引擎**。

## 核心模块 (Core Modules)

### 1. 智能意图路由 (AI Agent Architecture)
* **路径**: `AI_Agent_Architecture/`
* **核心**: `WealthRouter.cs`, `AIManager.cs`
* **逻辑**: 实现了 **Chain-of-Thought (思维链)** 分发机制，将自然语言转化为 `Stocks`, `Lottery`, `Resell` 等业务系统的原子指令。

### 2. 拟真经济引擎 (Economic Simulation)
* **路径**: `Economic_Simulation/`
* **核心**: `PriceEngine.cs`, `StockLimitPredictor.cs`
* **算法**: 基于 **Volatility Profile (波动率配置)** 和 **Normal Distribution (正态分布)** 模拟真实的市场情绪与价格涨跌，而非简单的随机数。

### 3. 工程化工具 (Utils)
* **路径**: `Utils/`
* **核心**: `SmallLLM.cs`, `JsonSchemaValidator.cs`
* **亮点**: 实现了 **智能重试 (Smart Retry)** 与 **结构化输出校验**，确保 AI 在游戏运行时的稳定性。

---
*Tech Stack: Unity, C#, OpenAI API, Newtonsoft.Json*

