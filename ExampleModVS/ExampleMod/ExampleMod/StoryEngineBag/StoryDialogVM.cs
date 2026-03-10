using ExampleMod.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ExampleMod.StoryEngineBag
{
    public class StoryDialogVM : ViewModel
    {
        private string _speakerName;
        private string _dialogueContent;
        private bool _isVisible;

        // 新增：选项列表相关
        private bool _areOptionsVisible;


        

        public MBBindingList<StoryOptionVM> _optionList { get; private set; }
        // --- 新增：冲突面板相关字段 ---
        private bool _isConflictInfoVisible;
        private string _conflictGoalText;
        private string _conflictProgressText; // 显示 "50/100"
        private float _conflictBarWidth;      // 控制进度条图形的宽度 (像素)
        private string _conflictPatienceText;
        // 定义进度条满宽时的像素值 (根据 XML 设计调整)
        public const float _maxProgressValue = 100.0f;
        // 当前对话的总进度上限(用于计算比例)
        public float _currentProgressValue = 0f;        // 当前对话的进度值
        // --- 新增：预测条字段 ---
        private float _predictionBarWidth;
        private bool _isPredictionVisible;
        private float _current_and_predictionBarWidth;
        private CancellationTokenSource _animationCts;
        //详情条
        private bool _isTraitTooltipVisible;
        private string _traitDescription;
        private float _traitTooltipMarginLeft; // 改用 MarginRight
                                               // [新增] 预测条锁定标记
        private bool _isPredictionLocked = false;
        private StoryTraitVM _currentTooltipTrait;


        public StoryTraitVM GenerateTrait(string name, string description, bool isRevealed)
        {
            return new StoryTraitVM(
                    name,
                    description,
                    isRevealed,
                    OnTraitHoverBegin, // 传入 VM 里的回调
                    OnTraitHoverEnd
                );
        }

        public float GetMaxProgressValue()
        {
            return _maxProgressValue; // 返回当前对话的总进度上限
        }
        public void LockPrediction()
        {
            _isPredictionLocked = true;
        }


        // 添加一个关闭事件
        public event Action OnDialogClosed;
        public StoryDialogVM()
        {
            SpeakerName = "no speaker name";
            DialogueContent = "no content";
            IsVisible = false;

            // 初始化列表
            OptionList = new MBBindingList<StoryOptionVM>();
            AreOptionsVisible = false;
            MaxProgressBarWidth = 1000.0f;
            // 初始化冲突面板
            IsConflictInfoVisible = false;

            TraitList = new MBBindingList<StoryTraitVM>();

            // 修改：传入回调函数 OnTraitHoverBegin 和 OnTraitHoverEnd
            TraitList.Add(new StoryTraitVM("贪婪", "此人极度贪财，只需给钱即可。", true, OnTraitHoverBegin, OnTraitHoverEnd));
            TraitList.Add(new StoryTraitVM("荣耀", "看重名誉，不可羞辱他。", false, OnTraitHoverBegin, OnTraitHoverEnd));
            TraitList.Add(new StoryTraitVM("鲁莽", "做事不计后果，容易被激怒。", true, OnTraitHoverBegin, OnTraitHoverEnd));
        }

        // --- 新增选项相关属性 ---

        [DataSourceProperty]
        public bool AreOptionsVisible
        {
            get => _areOptionsVisible;
            set
            {
                if (value != _areOptionsVisible)
                {
                    _areOptionsVisible = value;
                    OnPropertyChangedWithValue(value, "AreOptionsVisible");
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<StoryOptionVM> OptionList
        {
            get => _optionList;
            set
            {
                if (value != _optionList)
                {
                    _optionList = value;
                    OnPropertyChangedWithValue(value, "OptionList");
                }
            }
        }
        // 对应 XML 中的 SpeakerName
        [DataSourceProperty]
        public string SpeakerName
        {
            get => _speakerName;
            set
            {
                if (value != _speakerName)
                {
                    _speakerName = value;
                    OnPropertyChangedWithValue(value, "SpeakerName");
                }
            }
        }

        // 对应 XML 中的 DialogueContent
        [DataSourceProperty]
        public string DialogueContent
        {
            get => _dialogueContent;
            set
            {
                if (value != _dialogueContent)
                {
                    _dialogueContent = value;
                    OnPropertyChangedWithValue(value, "DialogueContent");
                }
            }
        }

        // 用于控制整个窗口显示/隐藏
        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (value != _isVisible)
                {
                    _isVisible = value;
                    OnPropertyChangedWithValue(value, "IsVisible");
                }
            }
        }

        [DataSourceProperty]
        public bool IsConflictInfoVisible
        {
            get => _isConflictInfoVisible;
            set
            {
                if (value != _isConflictInfoVisible)
                {
                    _isConflictInfoVisible = value;
                    OnPropertyChangedWithValue(value, "IsConflictInfoVisible");
                }
            }
        }

        [DataSourceProperty]
        public string ConflictGoalText
        {
            get => _conflictGoalText;
            set
            {
                if (value != _conflictGoalText)
                {
                    _conflictGoalText = value;
                    OnPropertyChangedWithValue(value, "ConflictGoalText");
                }
            }
        }

        [DataSourceProperty]
        public string ConflictProgressText
        {
            get => _conflictProgressText;
            set
            {
                if (value != _conflictProgressText)
                {
                    _conflictProgressText = value;
                    OnPropertyChangedWithValue(value, "ConflictProgressText");
                }
            }
        }

        // 核心：这个属性决定了进度条有多长
        [DataSourceProperty]
        public float ConflictBarWidth
        {
            get => _conflictBarWidth;
            set
            {
                if (Math.Abs(value - _conflictBarWidth) > 0.01f)
                {
                    _conflictBarWidth = value;
                    OnPropertyChangedWithValue(value, "ConflictBarWidth");
                }
            }
        }
        private float _maxProgressBarWidth;
        [DataSourceProperty]
        public float MaxProgressBarWidth
        {
            get => _maxProgressBarWidth;
            set
            {
                if (Math.Abs(value - _maxProgressBarWidth) > 0.01f)
                {
                    _maxProgressBarWidth = value;
                    OnPropertyChangedWithValue(value, "MaxProgressBarWidth");
                }
            }
        }


        // [新增] 预测条的宽度 (XML绑定: SuggestedWidth)
        [DataSourceProperty]
        public float PredictionBarWidth
        {
            get => _predictionBarWidth;
            set
            {
                if (Math.Abs(value - _predictionBarWidth) > 0.01f)
                {
                    _predictionBarWidth = value;
                    OnPropertyChangedWithValue(value, "PredictionBarWidth");
                }
            }
        }

        [DataSourceProperty]
        public float CurrentAndPredictionBarWidth
        {
            get => _current_and_predictionBarWidth;
            set
            {
                if (Math.Abs(value - _current_and_predictionBarWidth) > 0.01f)
                {
                    _current_and_predictionBarWidth = value;
                    OnPropertyChangedWithValue(value, "CurrentAndPredictionBarWidth");
                }
            }
        }

        // [新增] 预测条是否显示 (XML绑定: IsVisible)
        [DataSourceProperty]
        public bool IsPredictionVisible
        {
            get => _isPredictionVisible;
            set
            {
                if (value != _isPredictionVisible)
                {
                    _isPredictionVisible = value;
                    OnPropertyChangedWithValue(value, "IsPredictionVisible");
                }
            }
        }

        [DataSourceProperty]
        public StoryTraitVM CurrentTooltipTrait
        {
            get => _currentTooltipTrait;
            set
            {
                if (value != _currentTooltipTrait)
                {
                    _currentTooltipTrait = value;
                    OnPropertyChangedWithValue(value, "CurrentTooltipTrait");
                }
            }
        }
        [DataSourceProperty]
        public float TraitTooltipMarginLeft
        {
            get => _traitTooltipMarginLeft;
            set
            {
                if (Math.Abs(value - _traitTooltipMarginLeft) > 0.01f)
                {
                    _traitTooltipMarginLeft = value;
                    OnPropertyChangedWithValue(value, "TraitTooltipMarginLeft");
                }
            }
        }


        [DataSourceProperty]
        public bool IsTraitTooltipVisible
        {
            get => _isTraitTooltipVisible;
            set
            {
                if (value != _isTraitTooltipVisible)
                {
                    _isTraitTooltipVisible = value;
                    OnPropertyChangedWithValue(value, "IsTraitTooltipVisible");
                }
            }
        }


        [DataSourceProperty]
        public string ConflictPatienceText
        {
            get => _conflictPatienceText;
            set
            {
                if (value != _conflictPatienceText)
                {
                    _conflictPatienceText = value;
                    OnPropertyChangedWithValue(value, "ConflictPatienceText");
                }
            }
        }

        [DataSourceProperty]
        public string TraitDescription
        {
            get => _traitDescription;
            set
            {
                if (value != _traitDescription)
                {
                    _traitDescription = value;
                    OnPropertyChangedWithValue(value, "TraitDescription");
                }
            }
        }


        public void Close()
        {
            if (_animationCts != null)
            {
                _animationCts.Cancel();
                _animationCts.Dispose();
                _animationCts = null;
            }
            IsVisible = false;
            AreOptionsVisible = false;
            OptionList.Clear();
            IsConflictInfoVisible = false;
            HidePrediction(); // 清理预测状态
            // 触发关闭事件
            OnDialogClosed?.Invoke();

            //


        }
        public void ShowOptions(StoryOptionVM[] options)
        {
            OptionList.Clear();
            foreach (var opt in options)
            {
                OptionList.Add(opt);
            }
            AreOptionsVisible = true;
        }


        public void ExecuteClick()
        {
            if (AreOptionsVisible)
            {
                // 可以播放一个 "禁止" 的音效，或者无视
                return;
            }

           // InformationManager.DisplayMessage(new InformationMessage($"点击继续", Color.FromUint(0xFFFFFF)));

            // 通知引擎继续下一句
            StoryEngine.Instance.OnPlayerClick();
        }

        public void Show(string name, string content)
        {
            SpeakerName = name;
            DialogueContent = content;
            IsVisible = true;
        }

        public void UpdateConflictStatus(NegotiationState state,float predictedVal, bool shouldAnimate = true)
        {
            if (state == null)
            {
                // 如果没有状态，隐藏面板
                IsConflictInfoVisible = false;
                return;
            }

            // 1. 先开启面板可见性
            IsConflictInfoVisible = true;

            // 2. 更新静态文本 (立即生效)
            // 这里负责把复杂的数据格式化成人类能看的字符串
            ConflictGoalText = $"目标: {state.PlayerGoalDescription}"; // 例如："让对方赔偿"
            ConflictPatienceText = $"耐心: {state.MaxTurns-state.TurnCount}/{state.MaxTurns}";

            // 你甚至可以在这里根据耐心值改变颜色 (这也是封装的好处)
            // if (state.CurrentPatience < 20) { ... }

            // 3. 准备数值
            float fromVal = _currentProgressValue; // 动画起点：当前的进度
            float toVal = state.CurrentProgress;   // 动画终点：新的进度
            float maxVal = state.TargetThreshold;  // 最大值

            // 4. 决定是“动画过渡”还是“瞬间刷新”
            if (shouldAnimate)
            {
                // 调用底层的动画方法（Fire-and-forget，不等待它结束，避免阻塞主线程）
                // 注意：这里不需要 await，因为 UI 动画通常是独立运行的
                _ = AnimateProgressTo(fromVal, toVal, predictedVal, maxVal);
            }
            else
            {
                // 瞬间刷新（通常用于刚打开界面时的初始化）
                _currentProgressValue = toVal;

                // 手动更新一下进度条宽度
                float ratio = maxVal > 0 ? toVal / maxVal : 0f;
                ConflictBarWidth = MathF.Clamp(ratio, 0f, 1f) * MaxProgressBarWidth; // 假设满宽是 _maxProgressValue
                ConflictProgressText = $"{toVal:0}/{maxVal:0}";

            }
            
        }
        public void HideConflictInfo()
        {
            IsConflictInfoVisible = false;
        }

        // [新增] 供子选项调用：显示预测
        public void PreviewPrediction(float gainProgress,float maxProgress)
        {
            if (maxProgress <= 0) return;
            // 计算增量比例
            float gainRatio = gainProgress / maxProgress;

            // 限制：绿色 + 黄色 不能超过 100% (可选，看你想不想要爆表效果)
            // 这里我们只计算纯宽度的像素值
            float pixelWidth = MaxProgressBarWidth * gainRatio;

            // 边界检查：如果加上去超过了总长度，就把预测条截断
            if (ConflictBarWidth + pixelWidth > MaxProgressBarWidth)
            {
                pixelWidth = MaxProgressBarWidth - ConflictBarWidth;
                InformationManager.DisplayMessage(new InformationMessage($"预测溢出，截断宽度:总宽度{MaxProgressBarWidth}当前进度{ConflictBarWidth}预测进度增量{pixelWidth}", Color.FromUint(0xFFFFFF)));
            }
            if (pixelWidth < 0) pixelWidth = 0;

            PredictionBarWidth = pixelWidth;
            CurrentAndPredictionBarWidth = PredictionBarWidth + ConflictBarWidth;
            IsPredictionVisible = true;
        }

        // [新增] 供子选项调用：隐藏预测
        public void HidePrediction()
        {
            if (_isPredictionLocked) return;

            IsPredictionVisible = false;
            PredictionBarWidth = 0f;

        }

        public void ForceResetPrediction()
        {
            _isPredictionLocked = false;
            IsPredictionVisible = false;
            PredictionBarWidth = 0f;
            CurrentAndPredictionBarWidth = ConflictBarWidth; // 重置总宽
        }
        [DataSourceProperty]
        public MBBindingList<StoryTraitVM> TraitList { get; set; }




        private void OnTraitHoverBegin(StoryTraitVM trait)
        {
            // 1. 设置当前显示的数据
            CurrentTooltipTrait = trait;

            // 2. 计算位置
            int index = TraitList.IndexOf(trait);
            int totalCount = TraitList.Count;
            //InformationManager.DisplayMessage(new InformationMessage($"特征序号：{index}/{totalCount}", Color.FromUint(0xFFFFFF)));
            if (index >= 0)
            {
                TraitTooltipMarginLeft = 1000- (totalCount - index) * 85;
                TraitDescription = trait.Description; // 使用当前悬停的特征描述
                //InformationManager.DisplayMessage(new InformationMessage($"特征：坐标{TraitTooltipMarginLeft} 序号{index} 描述{TraitDescription}", Color.FromUint(0xFFFFFF)));
            }

            // 3. 显示面板
            IsTraitTooltipVisible = true;
        }

        private void OnTraitHoverEnd()
        {
            IsTraitTooltipVisible = false;
            // 可选：清空当前数据
            // CurrentTooltipTrait = null; 
        }

        public async Task AnimateProgressTo(float fromVal, float toVal, float predictedTotalVal, float maxVal)
        {
            // 1. 如果有正在播放的动画，先取消它，防止冲突
            if (_animationCts != null)
            {
                _animationCts.Cancel();
                _animationCts.Dispose();
            }
            _animationCts = new CancellationTokenSource();
            var token = _animationCts.Token;

            IsConflictInfoVisible = true;
            _isPredictionLocked = true;
            IsPredictionVisible = true;

            // === 计算动态时长 (阶段一：上涨) ===
            float growthAmount = Math.Abs(toVal - fromVal);
            float growthRatio = maxVal > 0 ? growthAmount / maxVal : 0f;

            // 逻辑：基础 1秒，根据占比最多增加到 2秒
            // Lerp(1.0, 2.0, ratio)
            float growthDuration = 1.0f + (growthRatio * 2.0f);
            // 钳制范围 (以防万一)
            growthDuration = MathF.Clamp(growthDuration, 1.0f, 3.0f);

            // === 动画循环 (基于时间，而非帧数) ===
            float startTime = Time.ApplicationTime;
            float endTime = startTime + growthDuration;

            // 像素比例 (用于计算预测条)
            float pixelPerUnit = maxVal > 0 ? MaxProgressBarWidth / maxVal : 0;

            try
            {
                while (Time.ApplicationTime < endTime)
                {
                    // 检查取消
                    if (token.IsCancellationRequested) return;

                    // 计算当前进度 t (0.0 ~ 1.0)
                    float t = (Time.ApplicationTime - startTime) / growthDuration;

                    // 可选：使用缓动函数 (EaseOut) 让动画末尾减速，更自然
                    // t = t * (2 - t); 

                    // 插值计算当前数值
                    float currentAnimVal = MathF.Lerp(fromVal, toVal, t);

                    // --- 更新 UI ---
                    float uiRatio = maxVal > 0 ? MathF.Clamp(currentAnimVal / maxVal, 0f, 1f) : 0f;
                    ConflictBarWidth = MaxProgressBarWidth * uiRatio;
                    _currentProgressValue = currentAnimVal;
                    ConflictProgressText = $"{currentAnimVal:0}/{maxVal:0}";

                    // 更新预测条 (视觉上被实际进度条顶着走或吃掉)
                    if(predictedTotalVal > maxVal)
                        predictedTotalVal = maxVal; // 防止溢出
                    float predictedPixelWidth = (predictedTotalVal - currentAnimVal) * pixelPerUnit;
                    if (predictedPixelWidth < 0) predictedPixelWidth = 0;

                    PredictionBarWidth = predictedPixelWidth;
                    CurrentAndPredictionBarWidth = ConflictBarWidth + PredictionBarWidth;

                    // 约 60FPS 刷新
                    await Task.Delay(16, token);
                }
            }
            catch (TaskCanceledException)
            {
                return; // 如果被取消，直接退出，不执行后续修正
            }

            // 强制对齐最终数值
            if (token.IsCancellationRequested) return;
            float finalRatio = maxVal > 0 ? MathF.Clamp(toVal / maxVal, 0f, 1f) : 0f;
            ConflictBarWidth = MaxProgressBarWidth * finalRatio;
            ConflictProgressText = $"{toVal:0}/{maxVal:0}";


            // === 阶段二：回缩动画 (如果预测条还有残留) ===
            // 场景：实际效果(toVal) < 预测效果(predictedTotalVal)
            // 此时 PredictionBarWidth 还剩下一截，需要缩回去
            if (PredictionBarWidth > 1f) // 只有残留明显才播放
            {
                // 停顿一下，让玩家看清"没达到预期"
                await Task.Delay(200, token);

                float startShrinkWidth = PredictionBarWidth;

                // === 计算回缩动态时长 ===
                // 假设回缩满条需要 1.5秒，最短 0.5秒
                float shrinkRatio = startShrinkWidth / MaxProgressBarWidth;
                float shrinkDuration = 0.5f + (shrinkRatio * 1.0f);
                shrinkDuration = MathF.Clamp(shrinkDuration, 0.5f, 1.5f);

                float shrinkStartTime = Time.ApplicationTime;
                float shrinkEndTime = shrinkStartTime + shrinkDuration;

                try
                {
                    while (Time.ApplicationTime < shrinkEndTime)
                    {
                        if (token.IsCancellationRequested) return;

                        float t = (Time.ApplicationTime - shrinkStartTime) / shrinkDuration;
                        // t = t * t; // EaseIn 加速回缩?

                        // 插值宽度
                        PredictionBarWidth = MathF.Lerp(startShrinkWidth, 0f, t);
                        CurrentAndPredictionBarWidth = ConflictBarWidth + PredictionBarWidth;

                        await Task.Delay(16, token);
                    }
                }
                catch (TaskCanceledException) { return; }
            }

            // === 结束 ===
            // 只有当没有被锁定的情况下（比如用户还没把鼠标放到新选项上），才重置
            // 但因为我们是非阻塞的，这里直接重置比较安全，防止残留
            if (!token.IsCancellationRequested)
            {
                ForceResetPrediction();
            }
        }
    }

    public class StoryOptionVM : ViewModel
    {
        private string _optionText;
        private Action _onExecute;
        private string _predictText; // 新增：后果预测文本
        private bool _shouldExpand;  // 新增：是否显示后果面板

        public Action _onHoverBeginAction;
        public Action _onHoverEndAction;
        // 用于查找特定按钮的唯一标识符 (例如 "MIND_READ_BTN")
        public string Identifier { get; private set; }
        private float _predictedProgressGain; // 该选项预计增加多少进度

        public StoryOptionVM(string text, Action onExecute, string predictText = "",   float predictedGain = 0,      Action onHoverBegin = null,         Action onHoverEnd = null   , string id = null)
        {
            _optionText = text;
            _onExecute = onExecute;
            _predictText = predictText;
            _shouldExpand = false;

            _onHoverBeginAction = onHoverBegin;
            _onHoverEndAction = onHoverEnd;
            _predictedProgressGain = predictedGain;
            Identifier = id;
        }

        [DataSourceProperty]
        public string OptionText
        {
            get => _optionText;
            set
            {
                if (value != _optionText)
                {
                    _optionText = value;
                    OnPropertyChangedWithValue(value, "OptionText");
                }
            }
        }
        // 新增：绑定后果文本（例如 "-500 第纳尔"）
        [DataSourceProperty]
        public string PredictText
        {
            get => _predictText;
            set
            {
                if (value != _predictText)
                {
                    _predictText = value;
                    OnPropertyChangedWithValue(value, "PredictText");
                }
            }
        }
        // 新增：控制后果面板的显示/隐藏
        [DataSourceProperty]
        public bool ShouldExpand
        {
            get => _shouldExpand;
            set
            {
                if (value != _shouldExpand)
                {
                    _shouldExpand = value;
                    OnPropertyChangedWithValue(value, "ShouldExpand");
                }
            }
        }
        // 新增：鼠标悬停开始
        public void ExecuteHoverBegin()
        {
            //InformationManager.DisplayMessage(new InformationMessage($"后果：{PredictText}", Color.FromUint(0xFFFFFF)));
            // 只有当有预测文本时才展开，避免空框出现
            if (!string.IsNullOrEmpty(PredictText))
            {
                ShouldExpand = true;
            }
            _onHoverBeginAction?.Invoke();
        }
        // 新增：鼠标悬停结束
        public void ExecuteHoverEnd()
        {
            ShouldExpand = false;
            _onHoverEndAction?.Invoke();
        }
        public void ExecuteOption()
        {
            // 执行传入的回调（告诉引擎选了哪一项）
            _onExecute?.Invoke();
        }
    }


    public class StoryTraitVM : ViewModel
    {
        private bool _isHovered;
        private string _iconSprite;
        private string _traitColor;
        private string _description;
        private string _name;
        // --- 新增：用于通知父级的委托 ---
        private Action<StoryTraitVM> _onHoverBeginAction;
        private Action _onHoverEndAction;

 
        public StoryTraitVM(string name, string description, bool isUnlocked, Action<StoryTraitVM> onHoverBegin, Action onHoverEnd)
        {
            // 如果未解锁，显示问号和锁图标；解锁后显示真名和开锁图标
            TraitName = isUnlocked ? name : "特征?";
            _description = description;

            IconSprite = isUnlocked ? "StdAssets\\lock_opened" : "StdAssets\\lock_closed";
            TraitColor = isUnlocked ? GetColorByTrait(name) : "#888888FF";

            _onHoverBeginAction = onHoverBegin;
            _onHoverEndAction = onHoverEnd;
        }

        // XML 绑定的属性
        [DataSourceProperty]
        public string TraitName
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, "TraitName"); } }
        }

        [DataSourceProperty]
        public string IconSprite
        {
            get => _iconSprite;
            set { if (value != _iconSprite) { _iconSprite = value; OnPropertyChangedWithValue(value, "IconSprite"); } }
        }

        [DataSourceProperty]
        public string TraitColor
        {
            get => _traitColor;
            set { if (value != _traitColor) { _traitColor = value; OnPropertyChangedWithValue(value, "TraitColor"); } }
        }

        [DataSourceProperty]
        public string Description
        {
            get => _description;
            set { if (value != _description) { _description = value; OnPropertyChangedWithValue(value, "Description"); } }
        }

        // === 关键：控制 Tooltip 显隐 ===
        [DataSourceProperty]
        public bool IsHovered
        {
            get => _isHovered;
            set
            {
                if (value != _isHovered)
                {
                    _isHovered = value;
                    OnPropertyChangedWithValue(value, "IsHovered");
                }
            }
        }

        // XML 中的 Command.HoverBegin="ExecuteHoverBegin" 会调用这个
        public void TraitHoverBegin()
        {
            IsHovered = true;
            // 通知父级：我(this)被悬停了
            _onHoverBeginAction?.Invoke(this);
        }

        // XML 中的 Command.HoverEnd="ExecuteHoverEnd" 会调用这个
        public void TraitHoverEnd()
        {
            IsHovered = false;
            // 通知父级：鼠标离开了
            _onHoverEndAction?.Invoke();
        }

        private string GetColorByTrait(string name)
        {
            if (name.Contains("贪婪")) return "#FF4444FF"; // 红
            if (name.Contains("鲁莽")) return "#FFD700FF"; // 金
            return "#44FF44FF"; // 绿
        }
    }
}
