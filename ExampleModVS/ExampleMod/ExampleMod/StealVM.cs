using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ViewModelCollection;
using TaleWorlds.PlayerServices;
using static TaleWorlds.Core.ItemObject;
namespace ExampleMod
{

    public class StealVM : ViewModel
    {
        private Agent _targetAgent;
        private EquipmentIndex? _targetSlotIndex; // 记录当前摸到了哪个槽位
        private Action _closeAction;

        //当前偷窃状态
        private string _centerTitleText;
        //物品描述
        private string _centerInfoText;
        //临时背包记录
        private string _lootLogText;
        //警惕条
        private string _suspicionText;
        private float _suspicionHeight;
        private const float MaxSuspicionHeight = 600f;
        private float _currentSuspicionValue = 0f; // 0 到 100
        //物品栏显示情况
        private bool _isItemRevealed;
        private bool _isItemHidden;
        private ImageIdentifierVM _currentItemImage;
        //可以摸索
        private bool _canInvestigate;
        // 被捉了
        private bool _isCaught = false;

        // 修复2：将 Random 提升为类成员，避免短时间内重复创建导致种子相同
        private Random _random = new Random();
        // [新增] 按钮文本字段
        private string _investigateBtnText;
        private string _takeBtnText;
        // [新增] 存储实际的风险概率 (0-100)
        private float _riskInvestigate; // 摸索的风险
        private float _riskTake;        // 拿走的风险

        public StealVM(Agent target, Action closeAction)
        {
            _targetAgent = target;
            _closeAction = closeAction;

            // 默认初始化
            IsItemHidden = true;
            IsItemRevealed = false;
            CanInvestigate = true;
            LootLogText = $"你把手伸入了{_targetAgent.Name}的包裹...";
            CenterTitleText = $"正在偷:{_targetAgent.Name}\n周遭环境安全，没人注意到你";
            CenterInfoText = "点击 [摸索] 来确认物品";
            CurrentItemImage = new ImageIdentifierVM(ImageIdentifierType.Item);

            // 初始化缺失的属性
            SuspicionText = "0%";
            SuspicionHeight = 0f;
            // [新增] 初始化按钮文本
            RecalculateState();
        }

        private void RecalculateState()
        {
            // 1. 获取基础数据
            float currentSus = _currentSuspicionValue;
            float roguerySkill = Hero.MainHero.GetSkillValue(DefaultSkills.Roguery);
            float skillReduction = roguerySkill * 0.1f; // 假设每级技能减少 0.1% 风险

            var witnesses = StealManager.GetWitnesses(Agent.Main, _targetAgent);
            int witnessCount = witnesses.Count;

            // 每一个目击者增加 30% 的基础发现概率（或者直接设为 100%）
            float witnessRisk = witnessCount * 30f;
            if (witnessCount > 0)
            {
                CenterTitleText = $"警告：有 {witnessCount} 双眼睛正盯着你！";
            }

            // 1. 计算【摸索/换一个】风险
            // 每次摸索大概增加10点警惕，基于此计算风险
            float predSusInv = MathF.Min(currentSus + 10f, 100f);
            float rawRiskInv = (predSusInv * 0.4f) + 5f - skillReduction;
            _riskInvestigate = MathF.Clamp(rawRiskInv+ witnessRisk, 0f, 100f);

            // 4. 计算【拿走】风险
            // 规则：拿走基于当前警惕，动作基础风险10，受重量影响
            float itemWeightRisk = 0f;
            if (_targetSlotIndex.HasValue && _targetAgent != null)
            {
                var itemElement = _targetAgent.SpawnEquipment[_targetSlotIndex.Value];
                if (!itemElement.IsEmpty)
                {
                    itemWeightRisk = itemElement.Weight * 2f; // 假设1kg增加2%风险
                }
            }
            float predSusTake = MathF.Min(currentSus + 20f, 100f);
            float rawRiskTake = (predSusTake * 0.4f) + 10f + itemWeightRisk - skillReduction;
            _riskTake = MathF.Clamp(rawRiskTake + witnessRisk, 0f, 100f); // 存入字段！

            // 5. 刷新 UI 文本 (直接读取刚才算好的字段)
            InvestigateBtnText = $"摸索\n({_riskInvestigate:0}%被发现)";

            // 拿取按钮特殊处理：如果没看到物品，显示未知
            if (_isItemRevealed)
                TakeBtnText = $"拿走\n({_riskTake:0}%被发现)";
            else
                TakeBtnText = "拿走\n(??%被发现)";

            // 3. 动态更新按钮文本
            if (_isItemRevealed)
            {
                // 如果已经显示了物品，按钮变成“摸下一个”
                InvestigateBtnText = $"摸下一个\n(风险{_riskInvestigate:0}%)";
                TakeBtnText = $"拿走\n(风险{_riskTake:0}%)";
            }
            else
            {
                // 如果没显示物品，按钮是“摸索”
                InvestigateBtnText = $"摸索\n(风险{_riskInvestigate:0}%)";
                TakeBtnText = "拿走\n(风险??%)";
            }
        }    
        // ================== 事件命令 (Command) ==================
        // [逻辑修正] 添加日志：新内容 + 换行符 + 旧内容 = 倒序显示
        private void AddLog(string message, string color = "#FFFFFF")
        {
            // 格式化：<Brush>...HTML风格</Brush> 或者是简单的文本
            // 这里假设支持简单的颜色标签，具体看游戏引擎支持
            string entry = $"<span color='{color}'>{message}</span>";

            // 关键点：新来的在最前面
            LootLogText = entry + "\n\n" + LootLogText;
        }
        private bool CheckDetection(float finalRiskChance)
        {
            if (_isCaught) return true;

            // 投骰子 (0-100)
            float roll = (float)(_random.NextDouble() * 100f);
            AddLog($"被发现检测: {roll}/({finalRiskChance})");  
            if (roll < finalRiskChance)
            {
                // 被抓了！
                _isCaught = true;
                CenterTitleText = $"你被对方发现了！";                
                AddLog("!!! 你的偷窃行为被发现了 !!!", "#FF0000"); // 红色高亮
                CanInvestigate = false;

                _ = OnStealFailed();

                return true;
            }

            return false;
        }
        // 按钮 1: 摸索 (Investigate)
        public void ExecuteInvestigate()
        {
            if (_targetAgent == null || _isCaught) return;
            // 1. 先进行风险判定 (摸索动作风险较小，比如 5%)
            // 即使警惕值很低，也有极小概率直接被抓
            if (IsItemRevealed)
            {
                AddLog("轻轻放下手中的东西，继续摸索...", "#CCCCCC");
                IsItemHidden = true;
                IsItemRevealed = false;
                _targetSlotIndex = null; // 清空当前选中
            }
            if (CheckDetection(_riskInvestigate))
            {
                return; // 被抓了，终止逻辑
            }
            IncreaseSuspicion(10.0f);//每次摸索增加10%警惕值// 1. 直接使用已经算好的概率进行判定
            // 3. 随机摸索结果
            // 使用类成员 _random
            int rng = _random.Next(0, 100);
            if (rng < 90) // 90% 几率摸到
            {
                _targetSlotIndex = StealManager.GetRandomStealableItemIndex(_targetAgent);

                if (_targetSlotIndex.HasValue)
                {
                    // 获取物品对象
                    var itemElement = _targetAgent.SpawnEquipment[_targetSlotIndex.Value];

                    // 1. 更新图片
                    ImageIdentifier imageID = new ImageIdentifier(itemElement.Item);
                    CurrentItemImage = new ImageIdentifierVM(imageID);
                    
                    float weight = itemElement.Item.Weight;
                    ItemTypeEnum type = itemElement.Item.Type;
                    string weightDesc = "轻";
                    string typeDesc = "武器";
                    if(weight < 2)
                    {
                        weightDesc = "轻";
                    }
                    else if(weight < 10)
                    {
                        weightDesc = "中";
                    }
                    else
                    {
                        weightDesc = "重";
                    }
                    switch (type)
                    {
                        case ItemTypeEnum.LegArmor:
                            typeDesc = "下身着装";
                            break;
                        case ItemTypeEnum.BodyArmor:
                            typeDesc = "上身着装";
                            break;
                        case ItemTypeEnum.HeadArmor:
                            typeDesc = "头部着装";
                            break;
                        case ItemTypeEnum.HandArmor:
                            typeDesc = "手部着装";
                            break;
                        case ItemTypeEnum.OneHandedWeapon:
                            typeDesc = "单手武器";
                            break;
                        case ItemTypeEnum.TwoHandedWeapon:
                            typeDesc = "双手武器";
                            break;
                        default:
                            typeDesc = "摸不出来";
                            break;
                    }

                    // 2. 更新文本
                    CenterInfoText = "摸到一件物品！\n" +
                        "掂量重量: " + weightDesc + "\n估摸类型：" + typeDesc;

                    // 3. 切换状态
                    IsItemHidden = false;
                    IsItemRevealed = true;
                }
                else
                {
                    CenterInfoText = "包里好像空了...";
                    AddLog("摸遍了也没找到东西。", "#FFaaaa");
                }
            }
            else
            {
                CenterInfoText = "什么也没摸到...";
                AddLog("手滑了，啥也没摸着。", "#AAAAAA");
            }
            RecalculateState();
        }

        // 按钮 2: 拿走 (Take)
        public void ExecuteTake()
        {
            if (_isCaught) return;
            if (CheckDetection(_riskTake))
            {
                return;
            }
            if (_targetSlotIndex.HasValue && _targetAgent != null)
            {
                var itemElement = _targetAgent.SpawnEquipment[_targetSlotIndex.Value];
                // 调用 Manager 执行偷窃逻辑
                string stolenItemName = StealManager.StealSpecificItem(_targetAgent, _targetSlotIndex.Value);
                IncreaseSuspicion(20.0f);//每次出手拿走增加20%警惕值
                if (!string.IsNullOrEmpty(stolenItemName))
                {
                    AddLog($"成功窃取: {stolenItemName}", "#00FF00"); // 绿色
                    // 偷完后重置，准备下一轮或者关闭
                    ResetRound();
                }
                CenterInfoText = "点击 [摸索] 来继续搜索物品";
            }
            RecalculateState();
        }

        

        // 按钮 3: 离开 (Leave)
        public void ExecuteLeave()
        {
            _closeAction?.Invoke();
        }

        private void ResetRound()
        {
            IsItemHidden = true;
            IsItemRevealed = false;
            _targetSlotIndex = null;
            CurrentItemImage = new ImageIdentifierVM(ImageIdentifierType.Item);
            CenterInfoText = "点击 [摸索] 继续寻找";
        }
        // [优化] 统一处理警惕值
        private void IncreaseSuspicion(float amount)
        {
            // 如果已经被抓了，就别再加了，防止重复触发
            if (_isCaught) return;
            _currentSuspicionValue += amount;
            if (_currentSuspicionValue > 100f) _currentSuspicionValue = 100f;

            // 更新文本
            SuspicionText = $"{_currentSuspicionValue:0}%";

            // 更新高度 (根据最大高度计算像素)
            // 假设父容器大约高 600px，这里按比例计算
            SuspicionHeight = (_currentSuspicionValue / 100f) * MaxSuspicionHeight;

            if (_currentSuspicionValue >= 100f)
            {
                // 【关键修改】在这里就标记被抓，阻断后续所有操作
                _isCaught = true;
                LootLogText += "\n[警告] 你动静被发现了！！！";
                // 这里应该触发游戏结束或战斗逻辑
                CenterTitleText = $"被{_targetAgent.Name}发现了！";

                _=OnStealFailed();
            }
        }
        private bool _hasFailedBroadcasted = false; // 新增一个标记字段
        private async Task OnStealFailed()
        {
            if (_hasFailedBroadcasted) return;
            _hasFailedBroadcasted = true;
            CenterTitleText = "你被抓现行了！";
            AddLog("!!! 偷窃失败，周围的人反应过来了 !!!", "#FF0000");

            // 2. 广播犯罪事件
            // 使用你现有的 AgentAIController
            AI.AgentAIController.Instance.BroadcastEventInRange(
                Agent.Main.Position,
                15f, // 广播半径
                "WitnessCrime",
                Agent.Main, // 罪犯
                _targetAgent // 受害者
            );
           
            // 3. 关闭 UI (或者延迟关闭，给玩家看一眼失败提示)
            // 这里建议用一个 Timer 延迟关闭，或者让玩家手动点离开，
            // 但为了无缝衔接质问，通常立即关闭 UI 比较好

            await Task.Delay(2000);


            _closeAction?.Invoke();
        }

        // ================== 数据绑定属性 (DataSourceProperty) ==================

        [DataSourceProperty]
        public string CenterTitleText
        {
            get => _centerTitleText;
            set { if (value != _centerTitleText) { _centerTitleText = value; OnPropertyChangedWithValue(value, "CenterTitleText"); } }
        }

        [DataSourceProperty]
        public string CenterInfoText
        {
            get => _centerInfoText;
            set { if (value != _centerInfoText) { _centerInfoText = value; OnPropertyChangedWithValue(value, "CenterInfoText"); } }
        }

        [DataSourceProperty]
        public string LootLogText
        {
            get => _lootLogText;
            set { if (value != _lootLogText) { _lootLogText = value; OnPropertyChangedWithValue(value, "LootLogText"); } }
        }

        [DataSourceProperty]
        public float SuspicionHeight
        {
            get => _suspicionHeight;
            set { if (value != _suspicionHeight) { _suspicionHeight = value; OnPropertyChangedWithValue(value, "SuspicionHeight"); } }
        }

        [DataSourceProperty]
        public bool IsItemRevealed
        {
            get => _isItemRevealed;
            set { _isItemRevealed = value; OnPropertyChanged("IsItemRevealed"); }
        }

        [DataSourceProperty]
        public bool IsItemHidden
        {
            get => _isItemHidden;
            set { _isItemHidden = value; OnPropertyChanged("IsItemHidden"); }
        }

        [DataSourceProperty]
        public ImageIdentifierVM CurrentItemImage
        {
            get => _currentItemImage;
            set { if (value != _currentItemImage) { _currentItemImage = value; OnPropertyChangedWithValue(value, "CurrentItemImage"); } }
        }

        [DataSourceProperty]
        public bool CanInvestigate
        {
            get => _canInvestigate;
            set { if (value != _canInvestigate) { _canInvestigate = value; OnPropertyChangedWithValue(value, "CanInvestigate"); } }
        }

        [DataSourceProperty]
        public string SuspicionText
        {
            get => _suspicionText;
            set
            {
                if (value != _suspicionText)
                {
                    _suspicionText = value;
                    OnPropertyChangedWithValue(value, "SuspicionText");
                }
            }
        }
        // ================== 新增 数据绑定属性 ==================
        [DataSourceProperty]
        public string InvestigateBtnText
        {
            get => _investigateBtnText;
            set { if (value != _investigateBtnText) { _investigateBtnText = value; OnPropertyChangedWithValue(value, "InvestigateBtnText"); } }
        }

        [DataSourceProperty]
        public string TakeBtnText
        {
            get => _takeBtnText;
            set { if (value != _takeBtnText) { _takeBtnText = value; OnPropertyChangedWithValue(value, "TakeBtnText"); } }
        }

    }
}
