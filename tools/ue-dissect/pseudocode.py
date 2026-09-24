# -*- coding: utf-8 -*-
"""把关键蓝图的每个图线性化成可读伪代码，落成 markdown，供人直接阅读。"""
import os, io, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import t3d_tools as T

from paths import DUMP_ROOT as OUT
DST = os.path.join(OUT, "digest", "PSEUDOCODE")
os.makedirs(DST, exist_ok=True)

GROUPS = {
    "01-法术基类.md": ["BP_AbilityParent"],
    "02-投射与变形.md": ["BP_ProjectileParent", "BP_Polymorph"],
    "03-引导族.md": ["BP_ChannelParent", "BP_ChannelSkyParent", "BP_HealChannelParent"],
    "04-放置与区域.md": ["BP_SpellPlacementParent", "BP_AreaAttackSingleParent",
                        "BP_AreaAttackTickParent", "BP_NSTriggerSpellAttackParent"],
    "05-符文与传送.md": ["BP_RuneParent", "BP_PhysicalRune", "BP_Teleport"],
    "06-移动法术.md": ["BP_MovingSpellParent", "BP_MovingDamagingSpell"],
    "07-自施法与增益治疗.md": ["BP_SelfCastSpell", "BP_BuffParent", "BP_HealSingleParent"],
    "08-召唤武器.md": ["BP_SpawnMagicItemParent"],
    "09-总控组件.md": ["MagicComponent"],
    "10-资源与增益组件.md": ["ResourceComponent", "BuffComponent", "TargetingComponent",
                            "CombatStatusComponent", "InputBufferComponent", "EquipmentComponent"],
    "11-AI施法任务.md": ["T_DetermineMagicSpell", "T_ChargeSpell", "T_FireSpell",
                        "T_FireChannelSpell", "T_ReleaseSpell", "D_ShouldTeleport",
                        "D_IsInRange", "S_MageStrafe", "S_SettingAIBehaviour",
                        "S_DetermineAttackType", "BP_AIController"],
    "12-动画通知.md": ["AN_TriggerMagicAbility", "AN_TriggerNextAbility",
                      "AN_InputBufferSwitch", "AN_ToggleAttackRotate", "AN_LungeMovementMode"],
    "13-敌人AI与工具库.md": ["BP_EnemyAI", "FunctionLibrary", "BP_NPC_Parent"],
}


def dump_bp(name, w):
    p = os.path.join(OUT, "t3d", "bp", name + ".t3d")
    if not os.path.exists(p):
        w.write("（缺资产 %s）\n\n" % name)
        return
    root, scopes, graphs = T.parse_t3d(p)
    gs = T.graphs_of(scopes)
    top = scopes.get(('root',), {}).get(name)
    w.write("\n---\n\n## 蓝图 %s\n\n" % name)
    if top:
        for pr in top['props']:
            if pr.startswith(('ParentClass', 'NewVariables')):
                w.write("```\n%s\n```\n" % pr[:900])
    for gname in sorted(gs):
        grp = gs[gname]
        if not grp:
            continue
        w.write("\n### 图：%s  （%d 节点）\n\n```\n" % (gname, len(grp)))
        try:
            for line in T.linearize(grp, max_steps=300):
                w.write(line + "\n")
        except Exception as e:
            w.write("<线性化失败 %r>\n" % e)
        w.write("```\n")


def main():
    for fname, bps in GROUPS.items():
        with io.open(os.path.join(DST, fname), "w", encoding="utf-8") as w:
            w.write("# 伪代码：%s\n\n" % fname[3:-3])
            w.write("> 由 T3D 导出自动线性化。`GET/SET X` = 读/写变量，`F()` = 调函数，"
                    "`[in: a=1, b=X]` = 输入引脚上的字面量，`-> then:` = 分支走向。\n")
            for bp in bps:
                dump_bp(bp, w)
    # 索引
    with io.open(os.path.join(DST, "README.md"), "w", encoding="utf-8") as w:
        w.write("# FCS 伪代码包（自动生成）\n\n")
        for fname, bps in GROUPS.items():
            w.write("- [`%s`](%s) —— %s\n" % (fname, fname, "、".join(bps)))
    print("PSEUDO_DONE", len(GROUPS))


if __name__ == '__main__':
    main()
