using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using OpenAI;
using CityAIUserInfo = CityAI.AI.Data.UserInfo;

namespace CityAI.AI.Core
{
    /// <summary>
    /// AI管理器（使用OpenAI插件版本）
    /// </summary>
    public class AIManager : MonoBehaviour
    {
        #region 单例模式
        
        private static AIManager instance;
        
        public static AIManager Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("AIManager");
                    instance = go.AddComponent<AIManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        #endregion

        [Header("配置")]
        [Tooltip("AI网络配置资源")]
        [SerializeField] private AINetConfig config;

        [Header("OpenAI API")]
        public OpenAIApi openai = new OpenAIApi("", "");  // 使用OpenAI插件

        [Header("状态")]
        [SerializeField] private bool isInitialized = false;
        [SerializeField] private bool isLoggedIn = false;

        private Dictionary<string, ChatSession> sessions = new Dictionary<string, ChatSession>();
        private CityAIUserInfo currentUser;

        #region 生命周期

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (config != null)
            {
                Initialize(config);
            }
        }
        #endregion

        #region 初始化和登录

        /// <summary>
        /// 初始化AI系统
        /// </summary>
        public void Initialize(AINetConfig config)
        {
            if (isInitialized)
            {
                Debug.LogWarning("[AIManage] 已经初始化");
                return;
            }

            this.config = config;

            if (!config.Validate())
            {
                Debug.LogError("[AIManager] 配置验证失败");
                return;
            }

            // 设置OpenAI API的路径
            OpenAIApi.BASE_PATH = config.serverUrl;
            OpenAIApi.USERE_PATH = config.serverUrl;
            OpenAIApi.AI_PATH = config.serverUrl;

            isInitialized = true;

            if (config.enableLogging)
            {
                Debug.Log($"[AIManager] 初始化成功，服务器地址: {config.serverUrl}");
            }
        }

        /// <summary>
        /// 用户登录（使用OpenAI插件的PerLogin方法）
        /// </summary>
        public async Task<bool> LoginAsync(string openid = null, string channel = "Visitor")
        {
            if (!isInitialized)
            {
                Debug.LogError("[AIManager] 未初始化");
                return false;
            }

            if (isLoggedIn)
            {
                Debug.LogWarning("[AIManager] 已经登录");
                return true;
            }

            if (string.IsNullOrEmpty(openid))
            {
                openid = SystemInfo.deviceUniqueIdentifier;
            }

            try
            {
                // 使用OpenAI插件的PerLogin方法
                PerLoginRequest request = new PerLoginRequest();
                request.Openid = openid;  // 注意是Openid，不是OpenID
                request.Channel = channel;
                request.Token = "";

                if (config.enableLogging)
                {
                    Debug.Log($"[AIManagerI] 开始登录... OpenID: {openid}, Channel: {channel}");
                }

                var response = await openai.PerLogin(request);

                if (response.Error == null && response.Data != null)
                {
                    currentUser = new CityAIUserInfo
                    {
                        id = response.Data.Id,  // 注意是Id，不是ID
                        nickname = response.Data.Nickname,
                        userkey = response.Data.Userkey,
                        point = response.Data.Point,
                        channel = response.Data.Channel
                    };

                    // 设置userkey
                    openai.SetConfiguration(currentUser.userkey, "");
                    
                    isLoggedIn = true;

                    if (config.enableLogging)
                    {
                        #if UNITY_EDITOR
                        // 开发环境：输出完整信息
                        Debug.Log($"[AIManager] 登录成功！Userkey: {currentUser.userkey}, 积分: {currentUser.point}");
                        #else
                        // 生产环境：只输出积分，不输出Userkey
                        Debug.Log($"[AIManager_OpenAI] 登录成功！积分: {currentUser.point}");
                        #endif
                    }

                    return true;
                }
                else
                {
                    string errorMsg = response.Error != null ? response.Error.Message : "未知错误";
                    Debug.LogError($"[AIManager] 登录失败：{errorMsg}");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AIManager] 登录异常：{e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// 自动登录
        /// </summary>
        public async Task<bool> AutoLoginAsync()
        {
            return await LoginAsync();
        }
        
        /// <summary>
        /// 强制登出（用于重新登录避免限流）
        /// </summary>
        public void ForceLogout()
        {
            if (config.enableLogging)
            {
                Debug.Log($"[AIManager] 🚪 强制登出：登出前 isLoggedIn={isLoggedIn}");
            }
            
            isLoggedIn = false;
            currentUser = null;
            
            if (config.enableLogging)
            {
                Debug.Log($"[AIManager] ✅ 强制登出成功：登出后 isLoggedIn={isLoggedIn}");
            }
        }
        
        /// <summary>
        /// 强制重新登录（跳过已登录检查）
        /// </summary>
        public async Task<bool> ForceLoginAsync(string openid = null, string channel = "Visitor")
        {
            if (config.enableLogging)
            {
                Debug.Log($"[AIManager] 🔄 ForceLoginAsync 开始，当前状态: isLoggedIn={isLoggedIn}");
            }
            
            // 先强制登出
            ForceLogout();
            
            if (config.enableLogging)
            {
                Debug.Log($"[AIManager] 🔄 ForceLogout 完成，当前状态: isLoggedIn={isLoggedIn}");
            }
            
            // 再重新登录
            bool result = await LoginAsync(openid, channel);
            
            if (config.enableLogging)
            {
                Debug.Log($"[AIManager] 🔄 LoginAsync 完成，结果: {result}, 当前状态: isLoggedIn={isLoggedIn}");
            }
            
            return result;
        }

        #endregion

        #region 会话管理

        /// <summary>
        /// 创建新的对话会话
        /// </summary>
        public ChatSession CreateSession(string systemPrompt = null, string sessionId = null)
        {
            if (!CheckLoginStatus())
                return null;

            string model = config != null ? config.chatModel : "qwen-turbo";
            ChatSession session = new ChatSession(openai, systemPrompt, model);

            string finalId = sessionId ?? session.SessionId;
            sessions[finalId] = session;

            if (config.enableLogging)
            {
                Debug.Log($"[AIManager_OpenAI] 创建会话: {finalId}, Model: {model}");
            }

            return session;
        }

        /// <summary>
        /// 获取指定会话
        /// </summary>
        public ChatSession GetSession(string sessionId)
        {
            if (sessions.TryGetValue(sessionId, out ChatSession session))
            {
                return session;
            }
            return null;
        }

        /// <summary>
        /// 删除会话
        /// </summary>
        public void RemoveSession(string sessionId)
        {
            sessions.Remove(sessionId);
        }

        /// <summary>
        /// 清除所有会话
        /// </summary>
        public void ClearAllSessions()
        {
            sessions.Clear();
        }

        #endregion

        #region 简化接口

        /// <summary>
        /// 发送单条消息（不保留历史）
        /// </summary>
        public async Task<string> SendMessageAsync(string message, string systemPrompt = null)
        {
            if (!CheckLoginStatus())
                return null;

            ChatSession tempSession = CreateSession(systemPrompt);
            string reply = await tempSession.SendAsync(message);
            RemoveSession(tempSession.SessionId);

            return reply;
        }

        #endregion

        #region 工具方法

        private bool CheckLoginStatus()
        {
            if (!isInitialized)
            {
                Debug.LogError("[AIManager] 未初始化");
                return false;
            }

            if (!isLoggedIn)
            {
                Debug.LogError("[AIManager] 未登录");
                return false;
            }

            return true;
        }

        public CityAIUserInfo GetUserInfo()
        {
            return currentUser;
        }

        public int GetUserPoints()
        {
            return currentUser?.point ?? 0;
        }

        public bool IsLoggedIn()
        {
            return isLoggedIn;
        }

        public bool IsInitialized()
        {
            return isInitialized;
        }

        public AINetConfig GetConfig()
        {
            return config;
        }

        public void SetConfig(AINetConfig newConfig)
        {
            if (!isInitialized)
            {
                this.config = newConfig;
            }
        }

        #endregion
    }
}

