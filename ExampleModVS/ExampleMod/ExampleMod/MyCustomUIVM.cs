using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Library;
using TaleWorlds.Engine;
using System.IO;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.TwoDimension;
using TaleWorlds.MountAndBlade;

namespace ExampleMod
{
    public class MyCustomUIVM : ViewModel
    {
        private string _titleText;
        private string _contentText;

        private TaleWorlds.Engine.Texture _myCustomImage;

        // 这个委托用于告诉上层 Layer 关闭自己
        public delegate void OnCloseEvent();
        public event OnCloseEvent OnClose;


        private MBBindingList<BubbleSayVM> _bubbles;   




        [DataSourceProperty]
        public MBBindingList<BubbleSayVM> Bubbles
        {
            get => _bubbles;
            set
            {
                if (value != _bubbles)
                {
                    _bubbles = value;
                    OnPropertyChangedWithValue(value, "Bubbles");
                }
            }
        }
        public MyCustomUIVM()
        {
            // 初始化数据
            _titleText = "Pure Color UI";
            _contentText = "This is content Text.";
            var fakeBubble = new BubbleSayVM(null);
            fakeBubble.PosX = 900; // 屏幕大概中间
            fakeBubble.PosY = 500;
            fakeBubble.IsVisible = true; // 确保它是可见的
            fakeBubble.Scale = 40; // 确保字号够大
            Bubbles = new MBBindingList<BubbleSayVM>();
            _bubbles.Add(fakeBubble);

            
           // _myCustomImage=  LoadImageTest("H:\\taikou.png");
            
        }

       public TaleWorlds.Engine.Texture LoadImageTest(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.Print($"[TextureLoader] 文件不存在: {filePath}");
                InformationManager.DisplayMessage(new InformationMessage($"TextureLoader] 文件不存在", Color.FromUint(0xFF0000)));
                return null;
            }

            try
            {
                // 读取PNG文件的字节数据
                byte[] imageData = File.ReadAllBytes(filePath);

                // 使用引擎API创建纹理
                TaleWorlds.Engine.Texture texture1 = TaleWorlds.Engine.Texture.CreateFromMemory(imageData);
                TaleWorlds.Engine.Texture texture2 = TaleWorlds.Engine.Texture.LoadTextureFromPath("taikou.png","H:\\");
                
                if (texture1 != null)
                    InformationManager.DisplayMessage(new InformationMessage($"TextureLoader ReadAllBytes 成功", Color.FromUint(0xFF0000)));
                if (texture2 != null)
                    InformationManager.DisplayMessage(new InformationMessage($"TextureLoader LoadTextureFromPath 成功", Color.FromUint(0xFF0000)));
                if(texture1 ==null && texture2 ==null)
                    InformationManager.DisplayMessage(new InformationMessage($"TextureLoader ReadAllBytes 和 LoadTextureFromPath 都失败了", Color.FromUint(0xFF0000)));
                return texture2;
            }
            catch (Exception ex)
            {
                Debug.Print($"[TextureLoader] 加载纹理失败: {ex.Message}");

                InformationManager.DisplayMessage(new InformationMessage($"[TextureLoader] 加载纹理失败", Color.FromUint(0xFF0000)));
                return null;
            }
        }




        // 绑定 XML 中的 @TitleText
        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set
            {
                if (value != _titleText)
                {
                    _titleText = value;
                    OnPropertyChangedWithValue(value, "TitleText");
                }
            }
        }

        [DataSourceProperty]
        public string ContentText
        {
            get => _contentText;
            set
            {
                if (value != _contentText)
                {
                    _contentText = value;
                    OnPropertyChangedWithValue(value, "ContentText");
                }
            }
        }

        [DataSourceProperty]
        public TaleWorlds.Engine.Texture MyCustomImage
        {
            get => _myCustomImage;
            set
            {
                if (_myCustomImage != value)
                {
                    _myCustomImage = value;
                    OnPropertyChanged("MyCustomImage");
                }
            }
        }

      

        public void ExecuteClose()
        {
            InformationManager.DisplayMessage(new InformationMessage($"监听到点击关闭", Color.FromUint(0xFF0000)));
            // 触发关闭事件
            OnClose?.Invoke();

        }
    }
}
