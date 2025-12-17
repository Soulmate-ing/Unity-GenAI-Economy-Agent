using System.Threading.Tasks;
using CityAI.Player.Model;
using CityAI.AI.Core;
using CityAI.AI.Data;

namespace CityAI.AI.Handlers
{
    /// <summary>
    /// 默认AI处理器（处理无关问题）
    /// </summary>
    public class DefaultAIHandler : IAISystemHandler
    {
        public string SystemId => "Default";
        
        private readonly FortuneReplyManager replyManager;
        
        public DefaultAIHandler()
        {
            replyManager = FortuneReplyManager.Instance;
        }
        
        public bool IsMatch(string question)
        {
            // 默认处理器匹配所有问题
            return true;
        }
        
        public async Task<string> HandleQueryAsync(string question, PlayerModel playerModel)
        {
            // 返回Excel存储的无关问题回复
            if (replyManager != null)
            {
                string reply = replyManager.GetRandomReply("irrelevant");
                if (!string.IsNullOrEmpty(reply))
                {
                    return reply;
                }
            }
            
            return "抱歉，我只回答财富相关问题";
        }

        public async Task<CityAI.AI.Router.Models.Snap> HandleQueryAsSnapAsync(string question, PlayerModel playerModel)
        {
            // 默认处理器不支持 Snap 输出
            return null;
        }
    }
}
