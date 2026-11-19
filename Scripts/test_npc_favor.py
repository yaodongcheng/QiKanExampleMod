"""Exercise production NPC favor accounting, save pages, story grant, spend and presentation.

Native hero/clock/notifications are doubles. The ledger, complete memory save manager,
grant branch, recruitment consumption and UI formatter run the production C# source.
"""
from pathlib import Path
import json
import copy
import re
import subprocess
from test_story_perception import block
from check_story_probe import story_dialogue_issues

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'ExampleModVS/ExampleMod/ExampleMod'
OUT = ROOT / 'Debug/offline/npc_favor_tests'


def favor_methods():
    source = (SRC/'Memory/SingNpcMemorySystem.cs').read_text(encoding='utf-8-sig')
    return source[source.index('public sealed class FavorRecord'):source.index('// NPC 个人资料')]


def main():
    read = lambda p: (SRC/p).read_text(encoding='utf-8-sig')
    program = (ROOT/'Scripts/npc_favor_harness.cs').read_text(encoding='utf-8-sig')
    replacement = {
        'FAVOR_METHODS': favor_methods(),
        'RESTORE_MEMORY': block(read('Memory/SingNpcMemorySystem.cs'), r'public void RestoreFromSave\('),
        'SAVE_ENTRY': block(read('Memory/AllNpcMemoryManager.cs'), r'public class NpcMemorySaveEntry'),
        'MEMORY_MANAGER': block(read('Memory/AllNpcMemoryManager.cs'), r'public static class AllNpcMemoryManager'),
        'SAVE_GUARD': block(read('Debug/SaveGuard.cs'), r'public static class SaveStringGuard'),
        'RESOURCE_ENTRY': block(read('Core/AgentControlHelper.cs'), r'public static bool ChangePlayerFavor\('),
        'UI_FORMAT': block(read('Interaction/NpcInfoVM.cs'), r'internal static string BuildFavorText\('),
        'HAS_FAVOR': block(read('Planner/Cooperation/CoopRecruitment.cs'), r'public static bool HasFavor\('),
        'SPEND_FAVOR': block(read('Planner/Cooperation/CoopRecruitment.cs'), r'public static bool ConsumeFavor\('),
        'GRANT_BRANCH': block(read('Interaction/StoryProbe/StoryDialogueRuntime.cs'), r'case "favor_grant":\s*(?=\{)'),
    }
    for key, value in replacement.items(): program = program.replace('/* '+key+' */', value)
    OUT.mkdir(parents=True, exist_ok=True)
    project = OUT/'Favor.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
        '<NoWarn>0649;0162</NoWarn></PropertyGroup><ItemGroup><Compile Include="Program.cs" />'
        '<PackageReference Include="Newtonsoft.Json" Version="13.0.3" /></ItemGroup></Project>', encoding='utf-8')
    path = OUT/'Program.cs'
    path.write_text(program, encoding='utf-8')
    def run(show=True):
        result = subprocess.run(['dotnet','run','--project',str(project),'-c','Debug','-v','q'],
                                capture_output=True, text=True, encoding='utf-8', errors='replace')
        if show:
            print(result.stdout[-3500:])
            if result.returncode: print(result.stderr[-2500:])
        return result
    if run().returncode: return 1
    # Two counterexamples: restore wrongly drops zero-balance receipts; spending without affordability.
    for label, before, after in [
        ('duplicate business source', 'if (previous != null) return previous.Amount == amount && previous.SourceKey == sourceKey;',
         'if (previous != null) { }'),
        ('insufficient balance', 'if (balance < 0 || balance > int.MaxValue) return false;',
         'if (balance > int.MaxValue) return false;')]:
        assert before in program
        path.write_text(program.replace(before, after), encoding='utf-8')
        result = run(show=False)
        assert result.returncode and 'FAIL:' in result.stderr, label
        print('PASS: mutation rejected: '+label)
    path.write_text(program, encoding='utf-8')
    content = json.loads((ROOT/'ModuleData/DesignData/Cooperation/cooperation.json').read_text(encoding='utf-8-sig'))
    dialogue = json.loads((ROOT/'ModuleData/DesignData/Dialogues/story_son.dialog.json').read_text(encoding='utf-8-sig'))
    nodes = {n['Id']:n for n in dialogue['Nodes']}
    assert content['Recruitment']['FavorCost'] == 5
    for node, prior, amount, guard in [('son_turnin_ok','quest_turn_in','2','quest'),('event_4_win','resolve_event','3','event')]:
        effects = nodes[node]['OnEnter']; grant = next(e for e in effects if e['Id']=='favor_grant')
        assert effects.index(grant) > next(i for i,e in enumerate(effects) if e['Id']==prior)
        assert grant['Params']['amount']==amount and guard in grant['Params'] and grant['Receipt']=='once'
    assert all(e['Id']!='favor_grant' for n in dialogue['Nodes'] if 'lose' in n['Id'] for e in n.get('OnEnter',[]))
    assert not story_dialogue_issues(dialogue, formal=True)
    for field, value in [('amount','0'),('amount','-2'),('event','0'),('source','bad source'),('key','unlocalized')]:
        invalid = copy.deepcopy(dialogue)
        reward = next(e for n in invalid['Nodes'] if n['Id']=='event_4_win' for e in n['OnEnter'] if e['Id']=='favor_grant')
        reward['Params'][field]=value
        assert any(issue['code']=='bad_param' for issue in story_dialogue_issues(invalid, formal=True)), (field,value)
    print('PASS: 5 invalid favor reward definitions rejected')
    assert 'activityId' in read('Planner/Cooperation/CoopRecruitment.cs')
    assert 'pending.Activity.Id' in read('Planner/Cooperation/CoopRequestRouter.cs')
    assert 'RestoreFavorRecords(before.FavorRecords)' in read('Interaction/StoryProbe/StoryDialogueRuntime.cs')
    assert 'DeserializeFavorPages(slot, favorPages)' in read('Core/MyBehavior.cs')
    print('PASS: production reward/spend/persistence/rollback wiring; no failure rewards')
    return 0


if __name__ == '__main__': raise SystemExit(main())
