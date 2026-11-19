#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Exercise actual cooperation runtime/router/view with explicit native doubles.

Outputs stay in Debug/offline; no game DLL is deployed. Native navigation,
occlusion, animation and Gauntlet rendering must still be accepted in game.
"""
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
from test_story_perception import block

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'ExampleModVS/ExampleMod/ExampleMod/Planner/Cooperation'
OUT = ROOT / 'Debug/offline/coop_runtime_tests'


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(project, 'PropertyGroup')
    for key, value in [('OutputType', 'Exe'), ('TargetFramework', 'net8.0'),
                       ('EnableDefaultCompileItems', 'false'), ('Nullable', 'disable')]:
        ET.SubElement(props, key).text = value
    items = ET.SubElement(project, 'ItemGroup')
    ET.SubElement(items, 'PackageReference', Include='Newtonsoft.Json', Version='13.0.3')
    view_source = (ROOT / 'ExampleModVS/ExampleMod/ExampleMod/ImChat/ImChatView.cs').read_text(encoding='utf-8-sig')
    gate = block(view_source, r'public static bool CanOpen\(')
    program = (ROOT / 'Scripts/coop_runtime_harness.cs').read_text(encoding='utf-8-sig')
    atomic = (ROOT / 'ExampleModVS/ExampleMod/ExampleMod/AI/Actions/AtomicAction.cs').read_text(encoding='utf-8-sig')
    follow = block(atomic, r'public class FollowAgentAction')
    # Only engine navigation calls are substituted; constructor, timing, progress,
    # hysteresis, interruption and completion are the production implementation.
    for pattern, replacement in [
        (r'private Vec3 CalculateIdealPosition\(', 'private Vec3 CalculateIdealPosition() => _target.Position;'),
        (r'private float ComputeRepathInterval\(', 'private float ComputeRepathInterval() => .15f;'),
        (r'private void MoveToTarget\(', 'private void MoveToTarget(Agent agent) { }')]:
        follow = follow.replace(block(follow, pattern), replacement)
    speech = (ROOT / 'ExampleModVS/ExampleMod/ExampleMod/AI/SpeechChannel.cs').read_text(encoding='utf-8-sig')
    program = program.replace('/* ACTUAL_COOPERATION_UI_GATE */', gate)
    program = program.replace('/* ACTUAL_FOLLOW_ACTION */', follow)
    program = program.replace('/* ACTUAL_BEGIN_FOLLOW */', block(atomic, r'public bool BeginFollow\('))
    brain = (ROOT / 'ExampleModVS/ExampleMod/ExampleMod/AI/AgentBrain.cs').read_text(encoding='utf-8-sig')
    program = program.replace('/* ACTUAL_PERFORMANCE_ARBITRATION */', block(brain, r'private bool HandlePerformanceEvent\('))
    for marker, pattern in [('ACTUAL_TRY_ACQUIRE', r'public static PerformanceAction TryAcquire\('),
                            ('ACTUAL_RELEASE_LEASE', r'public void Release\('),
                            ('ACTUAL_PAUSE_LEASE', r'public void Pause\(')]:
        program = program.replace('/* ' + marker + ' */', block(atomic, pattern))
    for marker, start in [('ACTUAL_PERFORMANCE_CONTROL', 'public bool CanControl =>'),
                          ('ACTUAL_CONTROLS_ACTOR', 'public bool Controls(Agent actor) =>')]:
        offset = atomic.index(start)
        program = program.replace('/* ' + marker + ' */', atomic[offset:atomic.index(';', offset)+1])
    program = program.replace('/* ACTUAL_SPEECH_DURATION */', block(speech, r'public static float EstimateDuration\('))
    (OUT / 'Program.cs').write_text(program, encoding='utf-8')
    ET.SubElement(items, 'Compile', Include=str(OUT / 'Program.cs'))
    for name in ['Core', 'Goap', 'Htn', 'Planner', 'Facts', 'Content', 'Interaction',
                 'Log', 'Director', 'State', 'MissionRuntime', 'RequestRouter', 'View']:
        ET.SubElement(items, 'Compile', Include=str(SRC / ('Coop' + name + '.cs')))
    path = OUT / 'Runtime.csproj'
    ET.ElementTree(project).write(path, encoding='utf-8', xml_declaration=True)
    env = dict(os.environ, PYTHONUTF8='1')
    command = ['dotnet', 'run', '--project', str(path), '-c', 'Release', '-v', 'q', '--',
               str(ROOT / 'ModuleData/DesignData/Cooperation/cooperation.json')]
    result = subprocess.run(command, capture_output=True, encoding='utf-8', env=env)
    print(result.stdout[-6000:])
    if result.returncode or 'RUNTIME_PASS' not in result.stdout:
        print(result.stderr[-3000:])
        return 1
    runtime_source = (SRC / 'CoopMissionRuntime.cs').read_text(encoding='utf-8-sig')
    router_source = (SRC / 'CoopRequestRouter.cs').read_text(encoding='utf-8-sig')
    runtime_item = next(node for node in items.findall('Compile')
                        if node.get('Include') == str(SRC / 'CoopMissionRuntime.cs'))
    router_item = next(node for node in items.findall('Compile')
                      if node.get('Include') == str(SRC / 'CoopRequestRouter.cs'))
    try:
        for label, mutated_program, mutated_runtime, expected in [
            ('lost persistent follow', program.replace('keepFollow: keepFollowing', 'keepFollow: false'),
             runtime_source, 'actual native follow survives twenty seconds'),
            ('cross-speaker interruption', program,
             runtime_source.replace('step.SpeechShown && time >= step.SpeechEndsAt', 'step.SpeechShown'),
             'initiator opening is read before guard challenges')]:
            (OUT / 'Program.cs').write_text(mutated_program, encoding='utf-8')
            (OUT / 'MutatedRuntime.cs').write_text(mutated_runtime, encoding='utf-8')
            runtime_item.set('Include', str(OUT / 'MutatedRuntime.cs'))
            ET.ElementTree(project).write(path, encoding='utf-8', xml_declaration=True)
            mutation = subprocess.run(command, capture_output=True, encoding='utf-8', env=env)
            if mutation.returncode == 0 or expected not in mutation.stderr:
                print('FAIL mutation was not detected: ' + label)
                print(mutation.stdout[-1000:] + mutation.stderr[-1000:])
                return 1
            print('PASS: regression rejects ' + label + ' (isolated output only)')
    finally:
        (OUT / 'Program.cs').write_text(program, encoding='utf-8')
        runtime_item.set('Include', str(SRC / 'CoopMissionRuntime.cs'))
        ET.ElementTree(project).write(path, encoding='utf-8', xml_declaration=True)
    try:
        mutated_router = router_source.replace('bool success = pending.Response?.Success == true;',
                                               'bool success = pending.Response?.Success == true;\n            pending.Lease.Release("reply_finished");')
        assert mutated_router != router_source
        (OUT / 'MutatedRouter.cs').write_text(mutated_router, encoding='utf-8')
        router_item.set('Include', str(OUT / 'MutatedRouter.cs'))
        ET.ElementTree(project).write(path, encoding='utf-8', xml_declaration=True)
        mutation = subprocess.run(command, capture_output=True, encoding='utf-8', env=env)
        if mutation.returncode == 0 or 'accepted reply hands the same live lease' not in mutation.stderr:
            print('FAIL mutation was not detected: release before receiver starts')
            print(mutation.stdout[-1000:] + mutation.stderr[-1000:])
            return 1
        print('PASS: regression rejects release before receiver starts (actual Brain arbitration; isolated output only)')
    finally:
        router_item.set('Include', str(SRC / 'CoopRequestRouter.cs'))
        ET.ElementTree(project).write(path, encoding='utf-8', xml_declaration=True)
    print('PASS: actual runtime/router/view; native movement, sight and rendering remain doubles')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
