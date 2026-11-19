"""Run production gate detection, completion and HUD; native position/sight/UI are explicit doubles."""
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET
from test_story_feedback import method
from test_story_perception import block

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'ExampleModVS/ExampleMod/ExampleMod'
OUT = ROOT / 'Debug/offline/story_gate_tests'


def main():
    host = (SRC / 'Interaction/StoryProbe/StoryProbeMissionBehavior.cs').read_text(encoding='utf-8-sig')
    # Execute the new player-only pipeline. Also check that the real second-stage caller
    # invokes it unconditionally rather than keeping the previous Performance gate.
    second = method(host, 'TickSecondPhase')
    assert re.search(r'^\s+TickPlayerGate\(player, dt\);', second, re.M)
    assert 'CheckHandsOnGate' not in second
    methods = '\n'.join(method(host, name) for name in (
        'GateAlong', 'GateLateral', 'TrackGateCrossing', 'TickPlayerGate',
        'CheckHandsOnGate', 'GateWatchers', 'IsGateObserved', 'CompletePrologue'))
    for name in ['PlayerHasCrossed', 'GoalCompleted']:
        methods += '\n' + re.search(r'public bool ' + name + r'\s*=>[\s\S]*?;', host).group()
    program = (ROOT / 'Scripts/story_gate_harness.cs').read_text(encoding='utf-8-sig')
    program = program.replace('/* PRODUCTION_GATE_METHODS */', methods)
    program = program.replace('/* PRODUCTION_GOAL_TEXT */', block(host, r'public static class LWNScreenGoalText'))
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'Program.cs').write_text(program, encoding='utf-8')
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(project, 'PropertyGroup')
    for key, value in [('OutputType', 'Exe'), ('TargetFramework', 'net8.0'),
                       ('EnableDefaultCompileItems', 'false'), ('NoWarn', '0649')]:
        ET.SubElement(props, key).text = value
    items = ET.SubElement(project, 'ItemGroup')
    ET.SubElement(items, 'Compile', Include=str(OUT / 'Program.cs'))
    ET.SubElement(items, 'Compile', Include=str(SRC / 'Interaction/StoryProbe/StoryGoalHud.cs'))
    path = OUT / 'Gate.csproj'
    ET.ElementTree(project).write(path, encoding='utf-8', xml_declaration=True)
    command = ['dotnet', 'run', '--project', str(path), '--verbosity', 'quiet']
    result = subprocess.run(command, capture_output=True, text=True, encoding='utf-8')
    print(result.stdout)
    if result.returncode:
        print(result.stderr)
        return result.returncode
    # Mutations prove the tests detect the observed bugs, without touching production files.
    for label, previous, replacement, expected in [
        ('Performance suppresses crossing',
         'if (!_gateOpen && player != null) CheckHandsOnGate(player);',
         'if (!_gateOpen && player != null && !AgentAIController.GetBrainForAgent(GetAgent("blocker")).IsPerforming) CheckHandsOnGate(player);',
         'Performance must not suppress a legal crouched crossing'),
        ('gate open mistaken for crossing',
         re.search(r'public bool PlayerHasCrossed\s*=>[\s\S]*?;', methods).group(),
         'public bool PlayerHasCrossed => _gateOpen && StoryProbeState.Data.ApproachResolved;',
         'opening a route outside is not a witnessed crossing')]:
        assert previous in program
        (OUT / 'Program.cs').write_text(program.replace(previous, replacement), encoding='utf-8')
        broken = subprocess.run(command, capture_output=True, text=True, encoding='utf-8')
        assert broken.returncode != 0 and expected in broken.stderr, label
        print('PASS: isolated mutation rejected: ' + label)
    (OUT / 'Program.cs').write_text(program, encoding='utf-8')
    hud = (SRC / 'Interaction/StoryProbe/StoryGoalHud.cs').read_text(encoding='utf-8-sig')
    broken_hud = hud.replace('(host.Active || host.GoalCompleted)', 'host.Active')
    assert broken_hud != hud
    (OUT / 'Hud.cs').write_text(broken_hud, encoding='utf-8')
    items.findall('Compile')[1].set('Include', str(OUT / 'Hud.cs'))
    ET.ElementTree(project).write(path, encoding='utf-8', xml_declaration=True)
    broken = subprocess.run(command, capture_output=True, text=True, encoding='utf-8')
    items.findall('Compile')[1].set('Include', str(SRC / 'Interaction/StoryProbe/StoryGoalHud.cs'))
    ET.ElementTree(project).write(path, encoding='utf-8', xml_declaration=True)
    assert broken.returncode != 0 and 'completed HUD stays visible' in broken.stderr
    print('PASS: isolated mutation rejected: completion hides HUD')
    for language in ['', 'CNs/']:
        texts = {n.get('id'): n.get('text') for n in ET.parse(
            ROOT / ('ModuleData/Languages/' + language + 'std_LivingWorldNpcs_strings.xml')).iter('string')}
        for key in ['LWN_story_goal_done', 'LWN_story_narr_done', 'LWN_story_journal_finish']:
            assert ('尚未开放' if language else 'not yet available') in texts[key], (language, key)
        assert texts['LWN_story_hud_done_title']
    print('PASS: bilingual ending states content availability without inventing a new quest')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
