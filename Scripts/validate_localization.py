#!/usr/bin/env python3
"""validate_localization.py -- Localization integrity validation script.

Usage:
  python Scripts/validate_localization.py              # Full check
  python Scripts/validate_localization.py --cs-only    # C# checks only
  python Scripts/validate_localization.py --xml-only   # XML checks only
  python Scripts/validate_localization.py --strict     # Warnings become errors
"""

import os
import re
import sys
import glob as glob_mod
from collections import defaultdict
# Windows 下强制 UTF-8 输出：默认 GBK 代码页会让重定向日志（regress_*.log）乱码（2026-08-08）
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

# ── Paths ──
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
CS_SOURCE_DIR = os.path.join(PROJECT_ROOT, "ExampleModVS", "ExampleMod", "ExampleMod")
LANGUAGES_DIR = os.path.join(PROJECT_ROOT, "ModuleData", "Languages")

# ── Known placeholder whitelist ──
KNOWN_PLACEHOLDERS = {
    "PLAYER", "SPEAKER", "SPEAKER_SELF", "SPEAKER_PLAYER_ADDR", "SPEAKER_EMOTION",
    "TARGET", "ITEM", "StolenItemName", "LOCATION",
    "EventTypeName", "CrimeVerb", "CrimeVerbPast", "CrimeVerbGerund", "CrimeScene",
    "VictimLabel", "AuthorityRole", "SeverityWord", "DefaultPenalty",
    "EventId", "StolenCount", "StolenItemDesc", "DiscoveryFacts", "StolenItemClause",
    "ActionDescription", "TargetHeroName", "TargetHeroIdentity", "TargetSettlementName",
    "LocationDetail",
    "DaysSinceEvent", "TimeWord", "DaysSinceDiscovery", "DaysRemaining", "InvestigationDuration",
    "PublicAwarenessWord", "InvestigationProgressWord",
    "SuspectName", "SuspectIdentity", "SuspectDescription",
    "SuspectIsPlayer", "SuspectIsUnknown", "InitiatorIsPlayer",
    "PlayerIsAccused", "PlayerIsNotAccused",
    "WitnessExist", "WitnessCount", "WitnessCountWord",
    "PrimaryWitnessName", "PrimaryWitnessIdentity", "PrimaryWitnessDesc",
    "WitnessesSilenced", "EvidenceExist", "EvidenceCount", "TopEvidenceDesc",
    "SpeakerName", "SpeakerIdentity", "SpeakerRole",
    "SpeakerSelfRef", "SpeakerPlayerAddr", "SpeakerEmotion",
    "SpeakerAttitudeWord", "SpeakerIsAuthority",
    "ListenerName", "ListenerIdentity",
    "ListenerIsThief", "ListenerIsSuspect", "ListenerIsDetective",
    "ConfrontClosingLine",
    "RestitutionCost", "RestitutionCostOnSpot", "RestitutionCostHaggle",
    "RestitutionBreakdown", "AlertFineCost", "BountyAmount",
    "THRESHOLD",
    "CharmReprieveUsed", "FailCount", "FailCountRemaining",
    "NPC", "WORLD", "TERM_LORD", "GIVER", "DEPOSIT", "DAYS", "PAYER",
    "INSTIGATOR", "VICTIM", "COUNT", "REWARD",
    "LWN_FINE", "LWN_SETTLEMENT", "LWN_LOCKUP", "LWN_DAYS", "LWN_DETENTION_TEXT",
    "GOLD_ICON",
    "PLAYER_NAME", "NPC_NAME",
    # ResolveCompound 显式变量（语序由 XML 控制）
    "DESC", "IDENTITY", "NAME", "SELF_REF",
    # Intent 对话（Accountability/Surrender）新增变量
    "GOLD", "NEED", "HAVE", "FACTS", "VILLAGE", "PLACE", "SUSPECT", "CURRENT",
    # Commission/Quest 系统变量
    "ACT1", "ACT2", "ACT3", "ACTION", "ADDR", "AMOUNT", "ANIMAL", "ASSAULT", "SUMMARY",
    "ATTACKER", "AUTHORITY", "AVAILABLE", "BASE", "BASECOST", "BECAUSE",
    "BRIBECOST", "BUDGET", "CAPTURED", "CASE", "CASE_LABEL", "CATEGORY",
    "CATNAME", "CHANCE", "CHARM", "CHARMEDCOST", "CHARMSKILL", "CHIPS",
    "CLAN", "CLOSURE", "CODE", "CONTROL", "COST", "CRIME", "DAMAGE",
    "DELTA", "DIFFICULTY", "DIR", "DISCOUNT", "DIST", "DONE", "ENEMY",
    "EVENT", "EVENT_DESC", "EXTRA", "FINALCOST", "FIRST", "FLAVOR",
    "GAIN", "GAP", "GOAL", "GRADE", "HAGGLECOST", "HARM", "HERO",
    "HEROES", "HINT", "HONOR", "HP", "HUSBAND", "ID", "INDEX", "ITEMS",
    "ITEM_NAME", "KEY", "KINDS", "LABEL", "LEFT", "LEVEL", "LINE", "LOC",
    "LOCKUP", "MAX", "MAXWIDTH", "MESSAGE", "MISSING", "MODEL", "MODIFIED",
    "MODIFY", "MONSTER",
    "MSG", "MULT", "MYLEVEL", "NAME1", "NAME2", "NAMES", "NARRATIVE",
    "NEEDED", "NEW", "NPCNAME", "NPLEVEL", "OFFER", "OWNER", "PARAM",
    "PARTY",
    "PATH", "PCT", "PEOPLE", "PERCENT", "PIN", "PIXEL", "PREDICTION",
    "PREFIX", "PRICE", "PROB", "PROGRESS", "QUEST", "RANSOM", "RELATION",
    "REMAINING", "REPLY", "ROLE", "ROLL", "SCENE", "SELF", "SETTLEMENT",
    "SEV", "SEVERITY", "SKILL", "SLOT", "SOURCES", "SPOUSE", "STEAL", "STEP",
    "TAIL", "TARGET_NAME", "TASK_DESC", "TASK_GIVEN", "TASK_MSG",
    "TCONTROL", "TERMS", "TEXT", "TGT", "THEFT", "TIER", "TIERDESC",
    "TIME", "TIMES", "TITLE", "TOOLTIP", "TOTAL", "TROOP", "TRUSTDELTA",
    "TRUSTDESC", "TVIGOR", "TYPE", "TYPES", "UNIT", "URL", "VALUE", "VERB",
    "VICTIM_LINE", "VIGOR", "WAGERED", "WEIGHT", "WHERECLAUSE", "WIFE",
    "WITNESS", "WITNESS_CLAUSE", "WORLDDESC", "WORLD_DESCRIPTION",
    # 自动世界观生成（LWN_worldbg_generate，2026-08-17）
    "LANG",
    "WOUND", "WOUNDED",
    # 🔴 2026-08-14（M4 risky 风险卡）：{RISK} = LLM risk_analysis 原文（LLM 生成文本豁免本地化）
    "RISK",
    # 🔴 2026-08-14 基线 WARN 清零：以下变量均为代码实际传值解析
    #（ResolveCompound 显式变量 / MBTextManager.SetTextVariable 引擎变量），白名单补登
    "COND", "THEN", "MODE", "PEER", "RESULT", "TOPIC",
    "LWN_COMPANION", "LWN_COMPANION_FINE",
    # Prompt template placeholders (NPCProfile / ResolveCompound)
    "AGE", "ALCOHOL", "AMBITION", "ARMIES", "CASTLES", "CLAN_NAME",
    "CULTURE", "CURRENCY", "DECEASED", "DESIRE", "DESIRE_TYPE", "ENEMIES",
    "FRIENDSHIP", "GENDER", "INF", "ISM", "JOB", "KINGDOM",
    "LIFE_GOAL", "MY", "OCC", "OCC1", "OCC2", "ORIGIN",
    "POWER", "RANK", "REL", "RENOWN", "RULER_REL",
    "SHORT_GOAL", "SPIRIT", "STATUS", "STRENGTH", "STYLE",
    "TEMPER", "TOWNS", "VAL", "WAR", "WEALTH", "WEAPON",
    "PCT", "TIER", "HP", "SPOUSE", "ROLE", "CLAN",
    # 2026-08-20 双桶对称修复后补登（SaveGuard 调试行/裁剪提示、UI 按键提示）
    "DETAIL", "DPAD", "KEYS", "LWN_DAYS_LEFT", "OPEN_KEY",
    # 2026-08-20 prompt 双语化迁移新增占位符（PromptBuilder 对话类）
    "ACTION_SPACE", "CONFLICT", "OPTION_TEXT", "INPUT", "ADDR",
    # 2026-08-20 谈判 prompt 迁移新增占位符
    "RATIO", "REACTION", "SCORE", "TURN", "TURNS", "TACTIC", "MOOD", "CHIP", "PATIENCE",
    "COUNT_H", "COUNT_L", "COST_TYPE", "NOTO", "REP",
    # 2026-08-20 称呼纪律/亲缘段迁移新增占位符
    "KIND", "PARENT", "PRONOUN", "BLOOD", "SELFTITLE", "AGE", "IDENTITY",
    # 2026-08-20 记忆类 prompt 迁移新增占位符
    "CALC", "FADING", "HISTORY", "MEMORY", "MERCY", "VALOR",
    # 2026-08-20 导演类 prompt 迁移新增占位符
    "ACCUSED", "ACCUSER", "BOOK", "GALLERY", "PERSONA", "QUOTE",
    # 2026-08-20 WorldFactProvider/14 文件迁移新增占位符
    "BATTLES", "CULT", "DIFF", "FACING", "LEADER", "MIN", "NUM", "POS", "RANGE", "STATE", "WINS",
    # 2026-08-20 WorldFactProvider Query 正文迁移新增占位符
    "ARMOR", "BOND", "CAPTOR", "CARAVANS", "DAY", "DAYNIGHT", "FLOOR", "FLOORS", "FOOD",
    "INFLUENCE", "LI", "LISTED", "MARK", "MARKS", "MERGED", "MORALE", "NAME_A", "NAME_B",
    "PARTS", "REGULARS", "SCALE", "SEASON", "SIDE", "SIEGE", "TOWN", "TROOPS", "VERDICT",
    "VILLAGES", "WAYS", "WEATHER", "WHERE", "WORKSHOPS", "ZONE", "ANCHOR",
}

# Files exempt from {=!} check (known legacy code pending migration)
ESCAPE_BANG_EXEMPT_FILES = {"PlayerDetentionBehavior.cs"}

# Regex patterns
KEY_PATTERN = re.compile(r'^LWN_[a-z]+_[a-z0-9_]+$')
PLACEHOLDER_PATTERN = re.compile(r'\{([A-Z][A-Z_0-9]*[A-Z0-9])\}')
CJK_PATTERN = re.compile(r'[一-鿿぀-ゟ゠-ヿ]')
LWN_KEY_PATTERN = re.compile(r'"LWN_[a-z0-9_]+"')
ESCAPE_BANG_PATTERN = re.compile(r'\{=!\}')

errors = []
warnings = []

def error(msg):
    errors.append(msg)
    try:
        print(f"  [ERROR] {msg}")
    except UnicodeEncodeError:
        print(f"  [ERROR] {msg.encode('ascii', errors='replace').decode('ascii')}")

def warn(msg):
    warnings.append(msg)
    try:
        print(f"  [WARN]  {msg}")
    except UnicodeEncodeError:
        print(f"  [WARN]  {msg.encode('ascii', errors='replace').decode('ascii')}")

def safe_repr(text, maxlen=60):
    """Safely represent text, truncating and avoiding encoding issues."""
    t = text[:maxlen] + ('...' if len(text) > maxlen else '')
    return t.encode('ascii', errors='replace').decode('ascii')


# ═══════════════════════════════════════════════════════════
# A: No hardcoded CJK in C# (non-comment, non-debug lines)
# ═══════════════════════════════════════════════════════════
def check_cs_no_hardcoded_cjk():
    print("\n--- A: No hardcoded CJK in C# strings ---")
    cs_files = glob_mod.glob(os.path.join(CS_SOURCE_DIR, "**", "*.cs"), recursive=True)

    # Files/dirs to fully skip (pure debug / test / LLM prompts / internal)
    skip_dirs = {'Debug', 'Properties'}
    skip_files = {
        'DebugLogger.cs', 'MyCommands.cs', 'DisplayMessageLoggerPatch.cs',
        'AddQuickInformationLoggerPatch.cs', 'VanillaConversationLoggerPatch.cs',
        'GameMenuLoggerPatch.cs', 'ShowInquiryLoggerPatch.cs',
        'QuestJournalLoggerPatch.cs', 'QuestStartLoggerPatch.cs',
        'QuestMemoryRecorderPatch.cs',
    }
    # Files where CJK is in LLM prompts / internal system text, not player-visible
    llm_internal_files = {
        'PromptBuilder.cs', 'NPCProfile.cs', 'LLMService.cs',
        'AIStoryAdapt.cs', 'AIStoryGenerator.cs',
        'SingNpcMemorySystem.cs', 'AllNpcMemoryManager.cs',
        'QuestRecord.cs', 'ChatMessage.cs', 'PlayerResources.cs',
        'RecentMemory.cs', 'PlayerGeneratedOption.cs',
        'NegotiationSystem.cs',  # LLM negotiation prompts, not UI
        'StoryEngine.cs', 'StoryContext.cs',
        'CommandManager.cs', 'LogicCommands.cs', 'SystemCommands.cs',
        'VisualCommands.cs', 'StageDirector.cs', 'Text2Anim.cs',
        'ReadStory.cs', 'DesignDataLoad.cs',
        'GroupStageManager.cs',
        'AiSuspendPatch.cs', 'AiPatrollingNullFix.cs',
        'DebugBehavior.cs', 'MyCustomUIVM.cs',
        'IssueFilterBehavior.cs', 'IssueFilterPatch.cs',
        'QuestConsequenceResolver.cs', 'QuestConsequenceBehavior.cs',
        'VanillaQuestMapping.cs', 'IssueFactory.cs',
        'SafeLordPartyComponent.cs', 'CustomPartyComponent.cs',
        # Remaining files confirmed as non-player-visible (LLM prompts, comments, debug, internal)
        'CommissionIntent.cs',  # LLM narrative prompts only
        'StealManager.cs',      # DebugLogger.Log only
        'MySubModule.cs',       # File dump output + Debug.WriteLine
        'WorldEvent.cs',        # CJK in comments only, not string literals
        'StoryDialogVM.cs',     # Trait name matching logic
        'HeroNemesisTracker.cs',# Legacy save compatibility literal
        'AttitudeSystem.cs',    # Comments only
        'CommissionData.cs',    # Comments only
        'SocialEventManager.cs',# Comments only
        'WorldEventDirector.cs',# Comments only
        'WorldEventSimulator.cs',# Debug log only
        'InteractionController.cs', # Already migrated / LLM prompts only
        'WorldFactProvider.cs', # LLM prompt assembly (SceneAwareness / RiskScene) only
        # 🔴 2026-08-14 基线清理（A 检查）：以下文件 CJK 全部为 LLM prompt 段 / 记忆行模板 /
        # IM 事件消息 / 内部判定词（玩家可见文本已全部走 LWN key）——铁律 13 LLM prompt 豁免
        'AgentBrain.cs',        # 记忆行模板（目击/攻击叙述，进 LLM prompt）
        'ImReplyService.cs',    # 同伴互动段 / 频道消息段（prompt 拼接）
        'AutonomyProposal.cs',  # 【行动提议】【频道近期消息】prompt 段
        'DialogueComponent.cs', # 身份/话题 prompt 段
        'ReactiveAgent.cs',     # 旁观话题/记忆模板（prompt 材料）
        'SceneSnapshot.cs',     # 【场景当前人员】prompt 段
        'ActionRegistry.cs',    # 动作词表描述（进计划轮 prompt）
        'PlanCommandFlow.cs',   # 意图→命令示例表（LLM 意图匹配材料）
        'SpeechChannel.cs',     # 说话 prompt 指令（SpeechPriority → 台词生成）
        'MyBehavior.cs',        # IM 事件消息（BroadcastPlayerEvent → NPC 记忆）
        'PlanGrammar.cs',       # 校验警告日志 / 意图标签（非玩家可见）
        'PersuadeSlot.cs',      # 说服 prompt 指令段 / 记忆行
        'CampaignSession.cs',   # 说服/动议 prompt 指令段 / 意图词表
        'AtomicAction.cs',      # 交战记忆旁白 / IM 事件消息
        'ImChatManager.cs',     # 关系档位/回应模式（群聊 prompt 段）/ 人设匹配词
        'ImMarchOrder.cs',       # 行军命令意图词表（LLM 匹配材料，中英双语）
        'ImTopicMatcher.cs',    # 称呼词表 + 话题评分日志（内部匹配/日志）
        'SessionDialogueTemplates.cs',  # 说服会话【态度】prompt 段
        'PlanReplan.cs',        # replan 指令 prompt
        # 🔴 2026-08-16（认知同步方案 A-T 新增文件）：感知记忆描述 / 事件描述 / 情绪句 / 旁白模板 /
        # 声称短语表 / 画像行 / 受困处境段——全部为 LLM prompt 材料或内部匹配词表（铁律 13 豁免）
        'PlayerMissionEventLogic.cs',  # 感知记忆描述（进 NPC 记忆 → prompt）/ 关切判定词
        'PartySplitFlow.cs',           # 分兵/归队旁白模板（RecordNarration → prompt）
        'ChatClaimChecker.cs',         # 口嗨声称短语表 + 守卫词（内部匹配词表）
        'ImEventBroadcaster.cs',       # 事件描述 + 情绪句（进 NPC 记忆 → prompt）
        'DistressFlow.cs',             # 受困处境 prompt 段 / 赎金勒索内部判定
        'PlayerImageStore.cs',         # 【主公的成色】画像行（prompt 材料）
        'AgentAIController.cs',        # 犯罪评论 desc（进 NPC 记忆 → prompt）
    }

    found = 0
    by_file = defaultdict(list)  # file -> [line numbers]

    for filepath in cs_files:
        relpath = os.path.relpath(filepath, PROJECT_ROOT)
        filename = os.path.basename(filepath)
        dirname = os.path.basename(os.path.dirname(filepath))

        if dirname in skip_dirs:
            continue
        if filename in skip_files:
            continue
        if filename in llm_internal_files:
            continue

        try:
            with open(filepath, 'r', encoding='utf-8') as f:
                lines = f.readlines()
        except UnicodeDecodeError:
            continue

        in_block_comment = False
        for lineno, line in enumerate(lines, 1):
            stripped = line.strip()

            # Skip comment lines
            if stripped.startswith('//'):
                continue
            if stripped.startswith('*') or stripped.startswith('/*'):
                continue
            if in_block_comment:
                if '*/' in stripped:
                    in_block_comment = False
                continue
            if '/*' in stripped and '*/' not in stripped:
                in_block_comment = True
                continue

            # Skip lines that are purely debug/log output (not player-visible).
            # 🔴 2026-08-14：行内任意位置含 DebugLogger（含 catch { DebugLogger.Log(...) } 同行写法）
            if re.search(r'DebugLogger\.(Log|LogError|LogWarning)', line):
                continue

            # Skip lwn-ignore markers
            if 'lwn-ignore: A' in line:
                continue

            # Extract string literals and check for CJK.
            # 🔴 2026-08-14：先剔除行尾注释（// 之后），防止注释里的 "中文" 被当字符串字面量提取
            #（如 `// 轮次上限（"聊天不会太长"）` 的注释内引号）。
            code_part = line.split('//', 1)[0] if '//' in line else line
            str_literals = re.findall(r'"([^"]*)"', code_part)
            for s in str_literals:
                if CJK_PATTERN.search(s):
                    by_file[relpath].append(lineno)
                    found += 1
                    break  # one per line

    # Report summary by file (limit per-file to avoid flood)
    for fpath in sorted(by_file):
        line_nums = by_file[fpath]
        unique = sorted(set(line_nums))
        if len(unique) <= 5:
            locs = ', '.join(str(n) for n in unique)
        else:
            locs = f"{unique[0]}-{unique[-1]} ({len(unique)} sites)"
        print(f"    {fpath}: {locs}")

    if found == 0:
        print("  [PASS] No hardcoded CJK in source strings")
    else:
        print(f"  [INFO] {found} hardcoded CJK sites in {len(by_file)} files (excl. debug/log)")
    return found


# ═══════════════════════════════════════════════════════════
# B: LWN_ key used in C# must have Chinese comment on previous line
# ═══════════════════════════════════════════════════════════
def check_cs_lwn_comment():
    print("\n--- B: LWN_ key Chinese comments ---")
    cs_files = glob_mod.glob(os.path.join(CS_SOURCE_DIR, "**", "*.cs"), recursive=True)
    total_keys = 0
    missing = 0
    for filepath in cs_files:
        relpath = os.path.relpath(filepath, PROJECT_ROOT)
        try:
            with open(filepath, 'r', encoding='utf-8') as f:
                lines = f.readlines()
        except UnicodeDecodeError:
            continue

        for i, line in enumerate(lines):
            match = LWN_KEY_PATTERN.search(line)
            if not match:
                continue
            total_keys += 1

            if 'lwn-ignore: B' in line:
                continue

            # Check previous non-empty line for CJK comment
            prev_line = ""
            for j in range(i - 1, -1, -1):
                prev = lines[j].strip()
                if prev:
                    prev_line = prev
                    break

            if not CJK_PATTERN.search(prev_line):
                print(f"    {relpath}:{i+1}: LWN_ key {match.group()} missing Chinese comment on prev line")
                missing += 1

    if missing == 0:
        print(f"  [PASS] LWN_ key comments OK ({total_keys} keys)")
    else:
        print(f"  [INFO] {missing}/{total_keys} keys missing Chinese comment")
    return missing


# ═══════════════════════════════════════════════════════════
# G: No new {=!} markers
# ═══════════════════════════════════════════════════════════
def check_cs_no_escape_bang():
    print("\n--- G: No new {=!} markers ---")
    cs_files = glob_mod.glob(os.path.join(CS_SOURCE_DIR, "**", "*.cs"), recursive=True)
    issues = 0
    for filepath in cs_files:
        relpath = os.path.relpath(filepath, PROJECT_ROOT)
        filename = os.path.basename(filepath)

        if filename in ESCAPE_BANG_EXEMPT_FILES:
            continue

        try:
            with open(filepath, 'r', encoding='utf-8') as f:
                lines = f.readlines()
        except UnicodeDecodeError:
            continue

        for lineno, line in enumerate(lines, 1):
            if ESCAPE_BANG_PATTERN.search(line):
                if 'lwn-ignore: G' in line:
                    continue
                print(f"    {relpath}:{lineno}: {{=!}} found, should be {{=LWN_*}}")
                issues += 1

    if issues == 0:
        print("  [PASS] No new {=!} markers")
    return issues


# ═══════════════════════════════════════════════════════════
# K: PlaceholderResolver has no hardcoded CJK in ResolveOne
# ═══════════════════════════════════════════════════════════
def check_cs_placeholder_resolver_clean():
    print("\n--- K: PlaceholderResolver ResolveOne clean ---")
    pr_path = os.path.join(CS_SOURCE_DIR, "Interaction", "Dialogue", "PlaceholderResolver.cs")
    if not os.path.exists(pr_path):
        warn("PlaceholderResolver.cs not found")
        return 0

    with open(pr_path, 'r', encoding='utf-8') as f:
        content = f.read()

    method_match = re.search(r'internal string ResolveOne.*?\{', content)
    if not method_match:
        return 0

    method_start = method_match.end()
    brace_depth = 0
    method_end = method_start
    for i in range(method_start, len(content)):
        if content[i] == '{':
            brace_depth += 1
        elif content[i] == '}':
            brace_depth -= 1
            if brace_depth == 0:
                method_end = i
                break

    method_body = content[method_start:method_end]
    issues = 0
    for match in re.finditer(r'return\s+"([^"]*)"', method_body):
        text = match.group(1)
        if CJK_PATTERN.search(text):
            snippet = safe_repr(text, 50)
            print(f"    PlaceholderResolver.cs ResolveOne: return \"{snippet}\" has hardcoded CJK")
            issues += 1

    if issues == 0:
        print("  [PASS] PlaceholderResolver ResolveOne is clean")
    return issues


# ═══════════════════════════════════════════════════════════
# XML Checks
# ═══════════════════════════════════════════════════════════

def extract_keys_from_xml(filepath):
    keys = []
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
    except Exception:
        return keys
    for m in re.finditer(r'<string\s+id="([^"]+)"', content):
        keys.append(m.group(1))
    return keys

def extract_keys_from_xml_with_text(filepath):
    results = []
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
    except Exception:
        return results
    for m in re.finditer(r'<string\s+id="([^"]+)"\s+text="([^"]*)"', content):
        results.append((m.group(1), m.group(2)))
    return results

def _collect_languages():
    """返回 [(lang_id, dir_path)]：English = 根目录（language_data 惯例 id），
    其余 = 各子目录（读各自 language_data.xml 的 id，读不到用目录名兜底）。
    🔴 2026-08-20：检查 C/D/E/F/H 原只遍历子目录，根目录 English 桶从不参与
    校验（English 桶缺 key 一直未被捕获）——统一走本 helper。"""
    result = [("English", LANGUAGES_DIR)]
    if not os.path.exists(LANGUAGES_DIR):
        return result
    for d in sorted(os.listdir(LANGUAGES_DIR)):
        full = os.path.join(LANGUAGES_DIR, d)
        if not os.path.isdir(full):
            continue
        lang_id = d
        ld_path = os.path.join(full, "language_data.xml")
        if os.path.exists(ld_path):
            try:
                import xml.etree.ElementTree as ET
                root = ET.parse(ld_path).getroot()
                if root is not None and root.get("id"):
                    lang_id = root.get("id")
            except Exception:
                pass
        result.append((lang_id, full))
    return result

def check_xml_key_completeness():
    print("\n--- C: XML key completeness across languages ---")
    lang_dirs = _collect_languages()
    if len(lang_dirs) <= 1:
        print(f"  [SKIP] Only {len(lang_dirs)} language(s), nothing to compare")
        return 0

    lang_keys = {}
    for lang, lang_path in lang_dirs:
        xml_files = glob_mod.glob(os.path.join(lang_path, "std_*.xml"))
        keys = set()
        for xf in xml_files:
            keys.update(extract_keys_from_xml(xf))
        lang_keys[lang] = keys

    all_keys = set().union(*lang_keys.values())
    issues = 0
    for lang, _ in lang_dirs:
        missing = all_keys - lang_keys[lang]
        for k in sorted(missing):
            error(f"{lang} missing key: {k}")
            issues += 1

    if issues == 0:
        print(f"  [PASS] All {len(lang_dirs)} languages have {len(all_keys)} keys")
    return issues

def check_xml_placeholder_consistency():
    print("\n--- D: Placeholder consistency across languages ---")
    lang_dirs = _collect_languages()
    if len(lang_dirs) <= 1:
        print(f"  [SKIP] Only {len(lang_dirs)} language(s)")
        return 0

    all_keys = defaultdict(lambda: defaultdict(set))
    for lang, lang_path in lang_dirs:
        for xf in glob_mod.glob(os.path.join(lang_path, "std_*.xml")):
            for key, text in extract_keys_from_xml_with_text(xf):
                ph = set(PLACEHOLDER_PATTERN.findall(text))
                all_keys[key][lang] = ph

    issues = 0
    for key, lang_phs in all_keys.items():
        if len(lang_phs) < 2:
            continue
        ref, ref_lang = None, None
        for lang, ph in lang_phs.items():
            if ref is None:
                ref, ref_lang = ph, lang
            elif ph != ref:
                if ref - ph:
                    error(f"{key}: {lang} missing placeholders {sorted(ref - ph)} (vs {ref_lang})")
                    issues += 1
                if ph - ref:
                    error(f"{key}: {lang} extra placeholders {sorted(ph - ref)} (vs {ref_lang})")
                    issues += 1

    if issues == 0:
        print("  [PASS] Placeholders consistent across languages")
    return issues

def check_xml_placeholder_whitelist():
    print("\n--- E: Placeholder whitelist ---")
    all_phs = set()
    for _, lang_path in _collect_languages():
        for xf in glob_mod.glob(os.path.join(lang_path, "std_*.xml")):
            for _, text in extract_keys_from_xml_with_text(xf):
                all_phs.update(PLACEHOLDER_PATTERN.findall(text))

    unknown = all_phs - KNOWN_PLACEHOLDERS
    for ph in sorted(unknown):
        warn(f"Unknown placeholder {{{ph}}} - check PlaceholderResolver registration")

    if not unknown:
        print(f"  [PASS] All {len(all_phs)} placeholders in whitelist")
    return len(unknown)

def check_xml_key_naming():
    print("\n--- F: Key naming (LWN_ convention) ---")
    issues = 0
    for _, lang_path in _collect_languages():
        for xf in glob_mod.glob(os.path.join(lang_path, "std_*.xml")):
            relpath = os.path.relpath(xf, PROJECT_ROOT)
            for key in extract_keys_from_xml(xf):
                if not KEY_PATTERN.match(key):
                    error(f"{relpath}: key \"{key}\" does not match LWN_ convention")
                    issues += 1
    if issues == 0:
        print("  [PASS] All keys follow LWN_ naming convention")
    return issues

def check_xml_no_duplicate_keys():
    print("\n--- H: No duplicate XML keys ---")
    issues = 0
    for _, lang_path in _collect_languages():
        seen = {}
        for xf in glob_mod.glob(os.path.join(lang_path, "std_*.xml")):
            relpath = os.path.relpath(xf, PROJECT_ROOT)
            for key in extract_keys_from_xml(xf):
                if key in seen:
                    error(f"{relpath}: key \"{key}\" duplicated (first in {seen[key]})")
                    issues += 1
                else:
                    seen[key] = relpath
    if issues == 0:
        print("  [PASS] No duplicate keys")
    return issues

def check_language_data_refs_exist():
    print("\n--- I: language_data.xml refs exist ---")
    issues = 0
    for lang_dir in glob_mod.glob(os.path.join(LANGUAGES_DIR, "*")):
        if not os.path.isdir(lang_dir):
            continue
        ld_path = os.path.join(lang_dir, "language_data.xml")
        if not os.path.exists(ld_path):
            continue
        lang_name = os.path.basename(lang_dir)
        try:
            with open(ld_path, 'r', encoding='utf-16') as f:
                content = f.read()
        except (UnicodeDecodeError, UnicodeError):
            try:
                with open(ld_path, 'r', encoding='utf-8') as f:
                    content = f.read()
            except Exception:
                warn(f"{lang_name}/language_data.xml: cannot read")
                continue
        refs = re.findall(r'xml_path="([^"]+)"', content)
        for ref in refs:
            full_path = os.path.join(LANGUAGES_DIR, ref)
            if not os.path.exists(full_path):
                error(f"{lang_name}/language_data.xml refs {ref} but file missing")
                issues += 1
    if issues == 0:
        print("  [PASS] language_data.xml refs all exist")
    return issues


def check_xml_strict_parse():
    """--- J: Strict XML well-formedness ---
    🔴 2026-08-15（实机事故复盘）：prompts XML 曾因未转义的双引号/尖括号整个解析失败
    （游戏侧 LWNTextHelper 加载 prompts 全灭 → LLM 请求残缺 → API 400 → 模板降级），
    而正则式 key 扫描（extract_keys_from_xml）不校验 XML 语法，漏报。
    本检查对 Languages/ 下全部 XML 做严格解析：任何一个文件语法错误 → 报错。
    （铁律 14 配套：emoji/BMP 外字符检查也在此处一并扫）"""
    import xml.etree.ElementTree as ET
    print("\n--- J: Strict XML well-formedness ---")
    issues = 0
    checked = 0
    files = []
    # 各语言子目录的 std_*.xml
    for lang_dir in glob_mod.glob(os.path.join(LANGUAGES_DIR, "*")):
        if os.path.isdir(lang_dir):
            files += glob_mod.glob(os.path.join(lang_dir, "std_*.xml"))
    # 根目录散 XML（英文 std_*.xml + language_data.xml，UTF-16 由 XML 声明自动识别）
    files += glob_mod.glob(os.path.join(LANGUAGES_DIR, "*.xml"))
    for xf in sorted(set(files)):
        checked += 1
        rel = os.path.relpath(xf, PROJECT_ROOT)
        try:
            ET.parse(xf)
        except Exception as e:
            error(f"{rel}: XML 语法错误: {e}")
            issues += 1
        # 铁律 14：emoji / BMP 外字符（引擎 UTF-16 解析器遇代理对直接崩语言加载）
        try:
            with open(xf, 'r', encoding='utf-8') as f:
                content = f.read()
        except UnicodeDecodeError:
            # language_data.xml 可能是 UTF-16 → 跳过字符级检查（语法已由 ET 校验）
            continue
        bad = [ch for ch in content if ord(ch) > 0xFFFF]
        if bad:
            shown = sorted({hex(ord(b)) for b in bad})
            error(f"{rel}: 含 {len(bad)} 个 BMP 外字符（铁律 14，会崩语言加载）: {', '.join(shown)}")
            issues += 1
    if issues == 0:
        print(f"  [PASS] All {checked} XML files well-formed, no non-BMP chars")
    return issues


# ═══════════════════════════════════════════════════════════
# Main
# ═══════════════════════════════════════════════════════════
def main():
    strict = '--strict' in sys.argv
    cs_only = '--cs-only' in sys.argv
    xml_only = '--xml-only' in sys.argv
    all_checks = not cs_only and not xml_only

    print("=== validate_localization.py ===")
    print(f"Project: {PROJECT_ROOT}")

    total_errors = 0
    total_warnings = 0

    if all_checks or cs_only:
        total_errors += check_cs_no_hardcoded_cjk()
        total_errors += check_cs_lwn_comment()
        total_errors += check_cs_no_escape_bang()
        total_errors += check_cs_placeholder_resolver_clean()

    if all_checks or xml_only:
        total_errors += check_xml_strict_parse()          # J：严格 XML 解析（语法 + 铁律 14 BMP 外字符）
        total_errors += check_xml_key_completeness()
        total_errors += check_xml_placeholder_consistency()
        total_warnings += check_xml_placeholder_whitelist()
        total_errors += check_xml_key_naming()
        total_errors += check_xml_no_duplicate_keys()
        total_errors += check_language_data_refs_exist()

    print(f"\n{'='*50}")
    print(f"Result: {total_errors} ERROR, {total_warnings} WARNING")
    if strict and total_warnings > 0:
        total_errors += total_warnings
        print("(strict mode: WARNING => ERROR)")
    if total_errors > 0:
        print("FAIL")
        sys.exit(1)
    else:
        print("PASS")
        sys.exit(0)

if __name__ == '__main__':
    main()
