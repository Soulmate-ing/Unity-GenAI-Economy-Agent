using UnityEngine;
using UnityEngine.UI;
using CityAI.UI;

namespace CityAI.UI.Phone
{
    /// <summary>
    /// 新版手机App按钮
    /// 使用UIManager统一管理App面板
    /// </summary>
    public class PhoneAppButton : MonoBehaviour
    {
        [Header("App配置")]
        [Tooltip("App ID（对应UIConst中的常量）")]
        public string appId;
        
        [Tooltip("App显示名称")]
        public string appName;
        
        [Tooltip("App图标")]
        public Sprite appIcon;
        
        [Header("UI组件")]
        [Tooltip("按钮组件")]
        public Button button;
        
        [Tooltip("图标显示")]
        public Image iconImage;
        
        [Tooltip("名称显示")]
        public TMPro.TextMeshProUGUI nameText;
        
        [Header("动画设置")]
        [Tooltip("是否启用点击动画")]
        public bool enableClickAnimation = true;
        
        [Tooltip("点击动画时长")]
        public float clickAnimationDuration = 0.1f;
        
        private void Start()
        {
            InitializeButton();
        }
        
        /// <summary>
        /// 初始化按钮
        /// </summary>
        private void InitializeButton()
        {
            // 自动获取组件
            if (button == null)
                button = GetComponent<Button>();
            
            if (iconImage == null)
                iconImage = GetComponentInChildren<Image>();
            
            if (nameText == null)
                nameText = GetComponentInChildren<TMPro.TextMeshProUGUI>();
            
            // 设置UI内容
            if (iconImage != null && appIcon != null)
                iconImage.sprite = appIcon;
            
            if (nameText != null && !string.IsNullOrEmpty(appName))
                nameText.text = appName;
            
            // 绑定点击事件
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OnAppButtonClick);
            }
        }
        
        /// <summary>
        /// App按钮点击事件
        /// </summary>
        public void OnAppButtonClick()
        {
            Debug.Log($"[PhoneAppButton] 点击App: {appName} ({appId})");
            
            // 播放点击动画
            if (enableClickAnimation)
            {
                PlayClickAnimation();
            }
            
            // 通过UIManager打开App面板
            OpenAppPanel();
        }
        
        /// <summary>
        /// 打开App面板
        /// </summary>
        private void OpenAppPanel()
        {
            // 根据appId打开对应的面板
            bool panelOpened = false;
            switch (appId)
            {
                case "StockMarket":
                    UIManager.Instance.OpenPanel(UIConst.StockMarketPanel);
                    panelOpened = true;
                    break;
                //case "Chat":
                //    UIManager.Instance.OpenPanel(UIConst.ChatPanel);
                //    panelOpened = true;
                    //break;
                default:
                    Debug.LogWarning($"[PhoneAppButton] 未知的App ID: {appId}");
                    break;
            }
            
            // 如果App面板成功打开，关闭手机面板
            if (panelOpened && UIManager.Instance != null)
            {
                // 检查手机面板是否打开
                if (UIManager.Instance.panelDict.ContainsKey(UIConst.PhonePanel))
                {
                    UIManager.Instance.ClosePanel(UIConst.PhonePanel);
                    Debug.Log($"[PhoneAppButton] App面板已打开，手机面板已自动关闭");
                }
            }
        }
        
        /// <summary>
        /// 播放点击动画
        /// </summary>
        private void PlayClickAnimation()
        {
            if (button != null)
            {
                // 简单的缩放动画
                var originalScale = button.transform.localScale;
                button.transform.localScale = originalScale * 0.9f;
                
                // 使用协程恢复缩放
                StartCoroutine(ResetScaleAfterDelay(originalScale));
            }
        }
        
        /// <summary>
        /// 延迟恢复缩放
        /// </summary>
        private System.Collections.IEnumerator ResetScaleAfterDelay(Vector3 originalScale)
        {
            yield return new WaitForSeconds(clickAnimationDuration);
            if (button != null)
            {
                button.transform.localScale = originalScale;
            }
        }
        
        /// <summary>
        /// 设置App信息
        /// </summary>
        public void SetAppInfo(string id, string name, Sprite icon)
        {
            appId = id;
            appName = name;
            appIcon = icon;
            
            // 更新UI显示
            InitializeButton();
        }
        
        /// <summary>
        /// 检查App是否已打开
        /// </summary>
        public bool IsAppOpen()
        {
            switch (appId)
            {
                case "StockMarket":
                    return UIManager.Instance.panelDict.ContainsKey(UIConst.StockMarketPanel);
                //case "Chat":
                //    return UIManager.Instance.panelDict.ContainsKey(UIConst.ChatPanel);
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// 更新按钮状态（可选：显示App是否已打开）
        /// </summary>
        public void UpdateButtonState()
        {
            if (button != null)
            {
                // 可以根据App是否打开来改变按钮外观
                bool isOpen = IsAppOpen();
                button.interactable = !isOpen; // 如果已打开，禁用按钮
                
                // 或者改变颜色
                var colors = button.colors;
                colors.normalColor = isOpen ? Color.green : Color.white;
                button.colors = colors;
            }
        }
    }
}
