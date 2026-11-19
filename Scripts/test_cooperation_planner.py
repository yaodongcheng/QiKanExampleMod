#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""随从协助破局 · 正式算法离线测试入口（实施计划 §6 / 测试说明 §4）。

用同一份 C# 规划实现（Planner/Cooperation/CoopCore|Goap|Htn|Planner.cs）驱动隔离测试工程：
  · GOAP 与 HTN 的可达性／成本必须与独立前向一致代价搜索（oracle）相同
  · 预算耗尽必须与"已穷尽无解"分开报告
  · 新增手段时 GOAP 能自己找到、HTN 未加方法必须诚实无解、补方法后得到同成本计划
  · 未知事实不得被当作假或真

产物只写模块根 Debug/offline，不部署游戏 DLL。
"""
import io
import json
import os
import subprocess
import sys
import xml.etree.ElementTree as ET
from test_story_perception import block

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SRC = os.path.join(ROOT, "ExampleModVS", "ExampleMod", "ExampleMod", "Planner", "Cooperation")
OUT = os.path.join(ROOT, "Debug", "offline", "coop_planner_tests")

CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <LangVersion>12.0</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>CoopPlannerTests</AssemblyName>
    <RootNamespace>CoopPlannerTests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <Compile Include="Program.cs" />
    <Compile Include="{core}" Link="CoopCore.cs" />
    <Compile Include="{goap}" Link="CoopGoap.cs" />
    <Compile Include="{htn}" Link="CoopHtn.cs" />
    <Compile Include="{planner}" Link="CoopPlanner.cs" />
    <Compile Include="{facts}" Link="CoopFacts.cs" />
    <Compile Include="{content}" Link="CoopContent.cs" />
    <Compile Include="{interaction}" Link="CoopInteraction.cs" />
    <Compile Include="{log}" Link="CoopLog.cs" />
    <Compile Include="{director}" Link="CoopDirector.cs" />
    <Compile Include="{state}" Link="CoopState.cs" />
  </ItemGroup>
</Project>
"""


def xml_escape(path):
    return path.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def check_natural_dialogue():
    path = os.path.join(ROOT, "ModuleData", "DesignData", "Dialogues", "story_son.dialog.json")
    with io.open(path, encoding="utf-8-sig") as f:
        nodes = {n["Id"]: n for n in json.load(f)["Nodes"]}
    def option(node, ident):
        return next(o for o in nodes[node]["Options"] if o["Id"] == ident)
    checks = []
    checks.append({"quest_turned", "event_resolved:4"}.issubset(option("son_chat", "coop")["Requires"]))
    checks.append("coop_favor_available" in option("son_coop_open", "temp")["Requires"])
    checks.append("coop_recruitable" in option("son_coop_open", "formal")["Requires"])
    checks.append({"coop_recruitable", "gold>=30"}.issubset(option("son_coop_price", "pay")["Requires"]))
    charm = option("son_coop_formal", "charm")
    checks.append("!asked:coop_recruit_charm" in charm["Requires"] and any(
        e["Id"] == "mark" and e["Params"].get("flag") == "coop_recruit_charm" for e in charm["Effects"]))
    checks.append(any(e["Id"] == "coop_recruit" and e["Params"].get("mode") == "charm"
                      for e in nodes["son_coop_charm_ok"]["OnEnter"]))
    checks.append(any(e["Id"] == "coop_recruit" and e["Params"].get("mode") == "paid"
                      for e in option("son_coop_price", "pay")["Effects"]))
    for parent, ident in [("son_coop_ready", "go"), ("son_coop_joined", "go"),
                          ("son_coop_charm_ok", "go"), ("son_coop_open", "start")]:
        edge = option(parent, ident)
        reply = nodes[edge["NextNode"]]
        checks.append("coop_available" in edge["Requires"] and not edge["Effects"]
                      and reply["Speaker"] == "npc" and bool(reply["TextKey"])
                      and not reply["Options"] and not reply["NextNode"]
                      and any(e["Id"] == "coop_start" and e["Commit"] == "on_session_end"
                              and e["Receipt"] == "session" for e in reply["OnEnter"]))
    cn = ET.parse(os.path.join(ROOT, "ModuleData", "Languages", "CNs", "std_LivingWorldNpcs_strings.xml"))
    cooperation_texts = [n.get("text", "") for n in cn.iter("string")
                         if n.get("id", "").startswith("LWN_coop_")
                         or (n.get("id", "").startswith("LWN_story_") and "coop" in n.get("id", ""))]
    checks.append(bool(cooperation_texts) and all("??" not in text and any("\u4e00" <= ch <= "\u9fff" for ch in text)
                                                 for text in cooperation_texts))
    if not all(checks):
        print("FAIL: natural dialogue route contracts: " + str([i + 1 for i, ok in enumerate(checks) if not ok]))
        return False
    print("PASS: %d natural dialogue route contracts (data wiring; game execution still requires acceptance)" % len(checks))
    return True


def check_recruitment_authorization():
    with io.open(os.path.join(SRC, "CoopRecruitment.cs"), encoding="utf-8-sig") as f:
        source = f.read()
    methods = "\n".join(block(source, pattern) for pattern in (
        r"public sealed class Eligibility",
        r"public static CoopContractMode GetMode\(Hero hero\)",
        r"public static bool HasFavor\(Hero hero\)",
        r"public static Eligibility Evaluate\(string candidateId, bool diagnostic = false\)",
        r"public static bool TrySetTemporary\(string candidateId, string purpose, double simTime, out string reason, bool diagnostic = false\)",
        r"private static bool EventSettled\(string actorId\)",
        r"public static bool CanCooperate\(string actorId, CoopActivity activity, out string reason\)",
        r"public static bool CanRequestPurpose\(string actorId, CoopActivity activity, string purpose, out string reason\)"))
    with io.open(os.path.join(HERE, "coop_recruitment_authorization_harness.cs"), encoding="utf-8") as f:
        program = f.read().replace("/* ACTUAL_AUTHORIZATION_METHODS */", methods)
    with io.open(os.path.join(SRC, "CoopCommands.cs"), encoding="utf-8-sig") as f:
        diagnostic = block(f.read(), r"public static class CoopTestSession")
    program = program.replace("/* ACTUAL_DIAGNOSTIC_SESSION */",
                              diagnostic.replace("TaleWorlds.CampaignSystem.Hero", "Hero"))
    with io.open(os.path.join(SRC, "CoopMissionBehavior.cs"), encoding="utf-8-sig") as f:
        host_source = f.read()
    host_methods = "\n".join(block(host_source, pattern) for pattern in (
        r"public override void OnCreated\(\)",
        r"public override void OnBehaviorInitialize\(\)",
        r"public override void AfterStart\(\)",
        r"public override void OnRemoveBehavior\(\)"))
    program = program.replace("/* ACTUAL_HOST_LIFECYCLE */", host_methods)
    startup = "\n".join(block(host_source, pattern) for pattern in (
        r"public CoopActivity StartCooperation\(",
        r"private CoopActivity StartCooperationCore\(",
        r"private static Dictionary<string, CoopTruth> BuildInitialFacts\(",
        r"private CoopRoleDef FindRole\(",
        r"internal static CoopGoalDef SelectRoleGoal\(",
        r"public static CoopAlgorithm ParseAlgorithm\("))
    startup += "\n#if DEBUG\n" + block(host_source, r"internal CoopActivity StartPreparedDiagnostic\(") + "\n#endif\n"
    program = program.replace("/* ACTUAL_STARTUP_METHODS */", startup)
    with io.open(os.path.join(SRC, "CoopCommands.cs"), encoding="utf-8-sig") as f:
        command = block(f.read(), r"private static string TestBegin\(")
    program = program.replace("/* ACTUAL_TEST_BEGIN */", command)
    auth_out = os.path.join(OUT, "authorization")
    os.makedirs(auth_out, exist_ok=True)
    project = os.path.join(auth_out, "Authorization.csproj")
    with io.open(project, "w", encoding="utf-8", newline="\n") as f:
        f.write('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                '<TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
                '</PropertyGroup><ItemGroup><Compile Include="Program.cs" /></ItemGroup></Project>')
    with io.open(os.path.join(auth_out, "Program.cs"), "w", encoding="utf-8", newline="\n") as f:
        f.write(program)
    for configuration in ("Debug", "Release"):
        run = subprocess.run(["dotnet", "run", "--project", project, "-c", configuration, "-v", "q"],
                             cwd=auth_out, capture_output=True, text=True)
        print(run.stdout[-3000:])
        if run.returncode != 0:
            print("FAIL: actual recruitment authorization (%s): " % configuration + run.stderr[-2000:])
            return False
    # 仅变异隔离产物：恢复旧的“追加行为不登记”缺陷，真实生命周期回归必须抓到。
    old_created = block(host_methods, r"public override void OnCreated\(\)")
    mutated = program.replace(old_created, old_created.replace("Current = this;", ""))
    with io.open(os.path.join(auth_out, "Program.cs"), "w", encoding="utf-8", newline="\n") as f:
        f.write(mutated)
    try:
        run = subprocess.run(["dotnet", "run", "--project", project, "-c", "Debug", "-v", "q"],
                             cwd=auth_out, capture_output=True, text=True)
        if run.returncode == 0 or "late-added host registered before AfterStart" not in run.stderr:
            print("FAIL: late-added host regression was not detected: " + run.stdout[-1000:] + run.stderr[-1000:])
            return False
        print("PASS: missing OnCreated registration mutation rejected (isolated output only)")
    finally:
        with io.open(os.path.join(auth_out, "Program.cs"), "w", encoding="utf-8", newline="\n") as f:
            f.write(program)
    # 撤掉quick专属入口后，真实启动逻辑必须重新暴露未观察拒绝，不能被启动替身掩盖。
    mutated = program.replace("behavior.StartPreparedDiagnostic(actorId, role, out reason, parsed)",
                              "behavior.StartCooperation(actorId, role, out reason, parsed, preview: true)")
    with io.open(os.path.join(auth_out, "Program.cs"), "w", encoding="utf-8", newline="\n") as f:
        f.write(mutated)
    try:
        run = subprocess.run(["dotnet", "run", "--project", project, "-c", "Debug", "-v", "q"],
                             cwd=auth_out, capture_output=True, text=True)
        if run.returncode == 0 or "actual first quick command reaches registered late-added host" not in run.stderr:
            print("FAIL: quick startup observation regression was not detected: " + run.stdout[-1000:] + run.stderr[-1000:])
            return False
        print("PASS: quick command using ordinary sight gate mutation rejected (isolated output only)")
    finally:
        with io.open(os.path.join(auth_out, "Program.cs"), "w", encoding="utf-8", newline="\n") as f:
            f.write(program)
    return True


def main():
    if not check_natural_dialogue():
        return 1
    missing = [f for f in ("CoopCore.cs", "CoopGoap.cs", "CoopHtn.cs", "CoopPlanner.cs")
               if not os.path.isfile(os.path.join(SRC, f))]
    if missing:
        print("FAIL: missing planner sources: %s" % missing)
        return 1
    os.makedirs(OUT, exist_ok=True)
    with io.open(os.path.join(OUT, "Harness.csproj"), "w", encoding="utf-8", newline="\n") as f:
        f.write(CSPROJ.format(
            core=xml_escape(os.path.join(SRC, "CoopCore.cs")),
            goap=xml_escape(os.path.join(SRC, "CoopGoap.cs")),
            htn=xml_escape(os.path.join(SRC, "CoopHtn.cs")),
            planner=xml_escape(os.path.join(SRC, "CoopPlanner.cs")),
            facts=xml_escape(os.path.join(SRC, "CoopFacts.cs")),
            content=xml_escape(os.path.join(SRC, "CoopContent.cs")),
            interaction=xml_escape(os.path.join(SRC, "CoopInteraction.cs")),
            log=xml_escape(os.path.join(SRC, "CoopLog.cs")),
            director=xml_escape(os.path.join(SRC, "CoopDirector.cs")),
            state=xml_escape(os.path.join(SRC, "CoopState.cs"))))
    with io.open(os.path.join(OUT, "Program.cs"), "w", encoding="utf-8", newline="\n") as f:
        f.write(io.open(os.path.join(HERE, "coop_planner_harness.cs"), encoding="utf-8").read())

    print("building isolated planner harness ...")
    build = subprocess.run(["dotnet", "build", os.path.join(OUT, "Harness.csproj"), "-c", "Release", "-v", "q"],
                           cwd=OUT, capture_output=True, text=True)
    if build.returncode != 0:
        print("FAIL: harness build failed")
        print(build.stdout[-4000:])
        print(build.stderr[-4000:])
        return 1

    run = subprocess.run(["dotnet", "run", "--project", os.path.join(OUT, "Harness.csproj"), "-c", "Release", "--no-build", "--",
                          os.path.join(ROOT, "ModuleData", "DesignData", "Cooperation", "cooperation.json")],
                         cwd=OUT, capture_output=True, text=True)
    out = run.stdout
    print(out[-6000:])
    if run.returncode != 0 or "ALL_LAYERS_PASS" not in out:
        print("FAIL: cooperation contract layers failed or harness produced no result")
        print(run.stderr[-4000:])
        return 1
    if not check_recruitment_authorization():
        return 1
    print("PASS: cooperation planner + lifecycle/attempt/persistence/config layers")
    return 0


if __name__ == "__main__":
    sys.exit(main())
