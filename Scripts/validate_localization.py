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
    "LOCKUP", "MAX", "MAXWIDTH", "MESSAGE", "MODIFIED", "MODIFY", "MONSTER",
    "MSG", "MULT", "MYLEVEL", "NAME1", "NAME2", "NAMES", "NARRATIVE",
    "NEEDED", "NEW", "NPCNAME", "NPLEVEL", "OFFER", "OWNER", "PARTY",
    "PATH", "PCT", "PEOPLE", "PERCENT", "PIN", "PIXEL", "PREDICTION",
    "PREFIX", "PRICE", "PROB", "PROGRESS", "QUEST", "RANSOM", "RELATION",
    "REMAINING", "REPLY", "ROLE", "ROLL", "SCENE", "SELF", "SETTLEMENT",
    "SEV", "SEVERITY", "SKILL", "SLOT", "SOURCES", "SPOUSE", "STEAL",
    "TAIL", "TARGET_NAME", "TASK_DESC", "TASK_GIVEN", "TASK_MSG",
    "TCONTROL", "TERMS", "TEXT", "TGT", "THEFT", "TIER", "TIERDESC",
    "TIME", "TIMES", "TITLE", "TOOLTIP", "TOTAL", "TROOP", "TRUSTDELTA",
    "TRUSTDESC", "TVIGOR", "TYPE", "TYPES", "UNIT", "VALUE", "VERB",
    "VICTIM_LINE", "VIGOR", "WAGERED", "WEIGHT", "WHERECLAUSE", "WIFE",
    "WITNESS", "WITNESS_CLAUSE", "WORLDDESC", "WORLD_DESCRIPTION",
    "WOUND", "WOUNDED",
    # Prompt template placeholders (NPCProfile / ResolveCompound)
    "AGE", "ALCOHOL", "AMBITION", "ARMIES", "CASTLES", "CLAN_NAME",
    "CULTURE", "DECEASED", "DESIRE", "DESIRE_TYPE", "ENEMIES",
    "FRIENDSHIP", "GENDER", "INF", "ISM", "JOB", "KINGDOM",
    "LIFE_GOAL", "MY", "OCC", "OCC1", "OCC2", "ORIGIN",
    "POWER", "RANK", "REL", "RENOWN", "RULER_REL",
    "SHORT_GOAL", "SPIRIT", "STATUS", "STRENGTH", "STYLE",
    "TEMPER", "TOWNS", "VAL", "WAR", "WEALTH", "WEAPON",
    "PCT", "TIER", "HP", "SPOUSE", "ROLE", "CLAN",
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

            # Skip lines that are purely debug/log output (not player-visible)
            if re.search(r'^\s*DebugLogger\.|^\s*Debug\.Print',
                         line):
                continue

            # Skip lwn-ignore markers
            if 'lwn-ignore: A' in line:
                continue

            # Extract string literals and check for CJK
            str_literals = re.findall(r'"([^"]*)"', line)
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

def check_xml_key_completeness():
    print("\n--- C: XML key completeness across languages ---")
    if not os.path.exists(LANGUAGES_DIR):
        warn("Languages dir not found")
        return 0

    lang_dirs = [d for d in os.listdir(LANGUAGES_DIR)
                 if os.path.isdir(os.path.join(LANGUAGES_DIR, d))]
    if len(lang_dirs) <= 1:
        print(f"  [SKIP] Only {len(lang_dirs)} language(s), nothing to compare")
        return 0

    lang_keys = {}
    for lang in lang_dirs:
        lang_path = os.path.join(LANGUAGES_DIR, lang)
        xml_files = glob_mod.glob(os.path.join(lang_path, "std_*.xml"))
        keys = set()
        for xf in xml_files:
            keys.update(extract_keys_from_xml(xf))
        lang_keys[lang] = keys

    all_keys = set().union(*lang_keys.values())
    issues = 0
    for lang in lang_dirs:
        missing = all_keys - lang_keys[lang]
        for k in sorted(missing):
            error(f"{lang} missing key: {k}")
            issues += 1

    if issues == 0:
        print(f"  [PASS] All {len(lang_dirs)} languages have {len(all_keys)} keys")
    return issues

def check_xml_placeholder_consistency():
    print("\n--- D: Placeholder consistency across languages ---")
    if not os.path.exists(LANGUAGES_DIR):
        return 0
    lang_dirs = [d for d in os.listdir(LANGUAGES_DIR)
                 if os.path.isdir(os.path.join(LANGUAGES_DIR, d))]
    if len(lang_dirs) <= 1:
        print(f"  [SKIP] Only {len(lang_dirs)} language(s)")
        return 0

    all_keys = defaultdict(lambda: defaultdict(set))
    for lang in lang_dirs:
        lang_path = os.path.join(LANGUAGES_DIR, lang)
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
    for lang_dir in glob_mod.glob(os.path.join(LANGUAGES_DIR, "*")):
        if not os.path.isdir(lang_dir):
            continue
        for xf in glob_mod.glob(os.path.join(lang_dir, "*.xml")):
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
    for lang_dir in glob_mod.glob(os.path.join(LANGUAGES_DIR, "*")):
        if not os.path.isdir(lang_dir):
            continue
        for xf in glob_mod.glob(os.path.join(lang_dir, "std_*.xml")):
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
    for lang_dir in glob_mod.glob(os.path.join(LANGUAGES_DIR, "*")):
        if not os.path.isdir(lang_dir):
            continue
        seen = {}
        for xf in glob_mod.glob(os.path.join(lang_dir, "std_*.xml")):
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
