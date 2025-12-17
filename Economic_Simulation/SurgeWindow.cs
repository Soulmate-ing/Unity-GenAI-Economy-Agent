using System;

namespace CityAI.ResaleSystem.PriceEngine
{
    /// <summary>
    /// 暴涨窗口数据
    /// </summary>
    [Serializable]
    public class SurgeWindow
    {
        public string DistrictId;        // 街区ID
        public string GoodsId;           // 暴涨商品ID
        public int StartHour;            // 开始时间（游戏小时）
        public int EndHour;              // 结束时间（游戏小时）
        public bool IsActive;            // 是否激活
        
        /// <summary>
        /// 检查指定小时是否在暴涨窗口内
        /// </summary>
        public bool IsInWindow(int hour)
        {
            return IsActive && hour >= StartHour && hour <= EndHour;
        }
        
        /// <summary>
        /// 获取暴涨持续时间
        /// </summary>
        public int GetDuration()
        {
            return EndHour - StartHour + 1;
        }
        
        public override string ToString()
        {
            return $"SurgeWindow[District={DistrictId}, Goods={GoodsId}, Hour={StartHour}-{EndHour}]";
        }
    }
}

