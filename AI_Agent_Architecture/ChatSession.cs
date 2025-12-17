using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using OpenAI;

namespace CityAI.AI.Core
{
    /// <summary>
    /// 对话会话管理（使用OpenAI插件版本）
    /// 简化版本：专注于基础对话功能
    /// </summary>
    public class ChatSession
    {
        private OpenAIApi api;
        private string systemPrompt;
        private string model;
        private List<ChatMessage> messageHistory;
        private int maxHistoryLength = 20;
        
        // 可配置参数（用于 SmallLLM）
        public float Temperature { get; set; } = 0.3f;
        public int MaxTokens { get; set; } = 200;

        public string SessionId { get; private set; }
        public int MessageCount => messageHistory.Count;

        public ChatSession(OpenAIApi api, string systemPrompt = null, string model = "qwen-plus")
        {
            this.api = api;
            this.systemPrompt = systemPrompt;
            this.model = model;
            this.messageHistory = new List<ChatMessage>();
            this.SessionId = System.Guid.NewGuid().ToString();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messageHistory.Add(new ChatMessage
                {
                    Role = "system",
                    Content = systemPrompt
                });
            }
        }

        /// <summary>
        /// 发送消息并获取回复（使用OpenAI插件的CreateChatCompletion）
        /// </summary>
        public async Task<string> SendAsync(string userMessage)
        {
            if (string.IsNullOrEmpty(userMessage))
            {
                Debug.LogWarning("[ChatSession_OpenAI] 消息内容为空");
                return null;
            }

            if (api == null)
            {
                Debug.LogError("[ChatSession_OpenAI] OpenAI API为null");
                return null;
            }

            // 添加用户消息到历史
            messageHistory.Add(new ChatMessage
            {
                Role = "user",
                Content = userMessage
            });

            try
            {
                // 使用OpenAI插件的方法
                var request = new CreateChatCompletionRequest
                {
                    Model = model,
                    Messages = new List<ChatMessage>(messageHistory),
                    Temperature = this.Temperature,  // 使用配置的温度
                    MaxTokens = this.MaxTokens,      // 使用配置的最大 Token
                    Stream = false
                };

                var response = await api.CreateChatCompletion(request);

                if (response.Choices != null && response.Choices.Count > 0)
                {
                    string aiReply = response.Choices[0].Message.Content;

                    // 添加AI回复到历史
                    messageHistory.Add(new ChatMessage
                    {
                        Role = "assistant",
                        Content = aiReply
                    });

                    TrimHistory();

                    return aiReply;
                }
                else
                {
                    // 移除失败的用户消息
                    messageHistory.RemoveAt(messageHistory.Count - 1);
                    Debug.LogError("[ChatSession_OpenAI] API返回空响应");
                    return null;
                }
            }
            catch (System.Exception e)
            {
                // 移除失败的用户消息
                if (messageHistory.Count > 0 && messageHistory[messageHistory.Count - 1].Role == "user")
                {
                    messageHistory.RemoveAt(messageHistory.Count - 1);
                }
                
                Debug.LogError($"[ChatSession_OpenAI] 发送消息异常：{e.Message}");
                return null;
            }
        }

        public void ClearHistory()
        {
            messageHistory.Clear();
            
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messageHistory.Add(new ChatMessage
                {
                    Role = "system",
                    Content = systemPrompt
                });
            }
        }

        public List<ChatMessage> GetHistory()
        {
            return new List<ChatMessage>(messageHistory);
        }

        public void SetMaxHistoryLength(int maxLength)
        {
            this.maxHistoryLength = Mathf.Max(1, maxLength);
            TrimHistory();
        }

        public void UpdateSystemPrompt(string newPrompt)
        {
            this.systemPrompt = newPrompt;
            messageHistory.RemoveAll(m => m.Role == "system");
            
            if (!string.IsNullOrEmpty(newPrompt))
            {
                messageHistory.Insert(0, new ChatMessage
                {
                    Role = "system",
                    Content = newPrompt
                });
            }
        }

        public string GetLastAIReply()
        {
            for (int i = messageHistory.Count - 1; i >= 0; i--)
            {
                if (messageHistory[i].Role == "assistant")
                    return messageHistory[i].Content;
            }
            return "";
        }

        public string GetLastUserMessage()
        {
            for (int i = messageHistory.Count - 1; i >= 0; i--)
            {
                if (messageHistory[i].Role == "user")
                    return messageHistory[i].Content;
            }
            return "";
        }

        private void TrimHistory()
        {
            if (messageHistory.Count <= maxHistoryLength)
                return;

            var systemMessages = messageHistory.Where(m => m.Role == "system").ToList();
            var otherMessages = messageHistory.Where(m => m.Role != "system").ToList();

            int keepCount = maxHistoryLength - systemMessages.Count;
            if (keepCount > 0 && otherMessages.Count > keepCount)
            {
                otherMessages = otherMessages.Skip(otherMessages.Count - keepCount).ToList();
            }

            messageHistory.Clear();
            messageHistory.AddRange(systemMessages);
            messageHistory.AddRange(otherMessages);
        }
    }
}

