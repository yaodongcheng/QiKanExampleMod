using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;

namespace LivingWorldNpcs.CampaignMode
{
    /// <summary>
    /// 认知状态批量命令（2026-09-16）。游戏内 `~` 控制台调用：
    ///   custom.meet_all          # 全体在世 Hero：HasMet（见过面）+ IsKnownToPlayer（知道他在哪）置位
    ///   custom.meet_all &lt;N&gt;      # 再顺手把「与主角的好感度」全体 +N（同原版 add_hero_relation 的加法口径）
    ///
    /// 🔴 为什么要有这条：原版 80 条 campaign.* 指令里**没有任何一条**能批量置位认知状态。
    ///    · `campaign.toggle_information_restrictions` 只解锁百科页的数值显示（走
    ///      DefaultInformationRestrictionModel.IsDisabledByCheat），**不置 HasMet、不影响对话**；
    ///    · `campaign.add_hero_relation All | N` 的 `All` 分支带 `Game.Current.IsDevelopmentMode` 门禁，
    ///      普通游玩不生效。
    ///
    /// 🔴 两个已知副作用（都已在实现里处理，改代码前先读）：
    ///   ① 置位会触发 HeroKnownInformationCampaignBehavior 的「你发现了 X」提示 —— **一个英雄一条**，
    ///      上千条会糊满左下角消息区（1.2.12 与 1.5.x 都注册了这个行为）→ 循环结束后
    ///      `InformationManager.ClearAllMessages()` 整批清掉（代价：玩家原有的消息列表也会被清空）。
    ///   ② HasMet 是**单向**的：引擎只暴露 `SetHasMet()`，`HasMet` 的 setter 是 private，
    ///      没有公开 API 能撤销 → 本命令只能置位，不能还原（要清只能读档）。
    /// </summary>
    public class HeroMeetCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("meet_all", "custom")]
        public static string MeetAll(List<string> args)
        {
            if (Campaign.Current == null || Hero.MainHero == null)
                return "Error: campaign not loaded.";

            // 🔴 首参【可弃占位】：骑砍2 的 CommandLineArgumentFunction 在完全不填参数时可能根本不触发，
            //    调试时习惯随手给个 "1"。解析不出数字就忽略并注明，绝不报错（CLAUDE.md 工作流约定）。
            int relationDelta = 0;
            string note = string.Empty;
            if (args != null && args.Count >= 1 && !string.IsNullOrWhiteSpace(args[0]))
            {
                int parsed;
                if (int.TryParse(args[0], out parsed))
                    relationDelta = parsed;
                else
                    note = $" [note: '{args[0]}' is not a number -> relation unchanged]";
            }

            int scanned = 0, met = 0, known = 0, related = 0;
            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero == null || hero.IsHumanPlayerCharacter)
                    continue;                       // 不给自己认识自己
                scanned++;

                if (!hero.HasMet)
                {
                    hero.SetHasMet();               // 引擎唯一入口（同时写 LastMeetingTimeWithPlayer）
                    met++;
                }
                if (!hero.IsKnownToPlayer)
                {
                    hero.IsKnownToPlayer = true;    // 公开 setter，会广播 OnPlayerLearnsAboutHero（= 副作用①）
                    known++;
                }
                if (relationDelta != 0)
                {
                    // 关系变更走原版 Action（与原版 add_hero_relation 同一条路径）
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(hero, Hero.MainHero, relationDelta);
                    related++;
                }
            }

            // 副作用①：上千条「你发现了 X」提示一次清掉
            InformationManager.ClearAllMessages();

            return $"OK: meet_all -> scanned={scanned} hasMet={met} known={known}"
                 + (relationDelta != 0 ? $" relation=+{relationDelta} ({related} heroes)" : " relation=unchanged")
                 + note + " (HasMet is one-way; message log cleared)";
        }
    }
}
