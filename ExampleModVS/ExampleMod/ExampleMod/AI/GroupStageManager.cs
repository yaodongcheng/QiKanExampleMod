using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.SaveSystem.Save;
using static TaleWorlds.Library.VirtualFolders.Win64_Shipping_Client;

namespace ExampleMod.AI
{
    public static class GroupStageManager
    {
        // 定义一个作为分配结果的结构体

        // === 调试开关 ===
        public static bool IsDebugMode = false;

        public class StagePoint
        {
            public Vec3 Position;       // 实际在 NavMesh 上的坐标
            public Vec2 LookDirection;  // 站在这里应该朝向哪里（通常指向圆心）
            public int LayerIndex;      // 第几圈（越小越靠前）
            public float Score;         // 评分（用于排序，比如距离偏离度）
            public bool IsOccupied;     // 是否被分配了
        }
        // 缓存分配结果：Key = 目标(Target), Value = { 围观者(Witness) -> 站位点(Spot) }
        private static Dictionary<Agent, Dictionary<Agent, StagePoint>> _allocationCache = new Dictionary<Agent, Dictionary<Agent, StagePoint>>();

        public static StagePoint GetAssignedSpot(Agent centerAgent, Agent witness)
        {
            if (_allocationCache.TryGetValue(centerAgent, out var assignments))
            {
                if (assignments.TryGetValue(witness, out var spot))
                {
                    return spot;
                }
            }
            return null;
        }
        public static void Reset(Agent centerAgent)
        {
            if (_allocationCache.ContainsKey(centerAgent)) _allocationCache.Remove(centerAgent);
        }
        
        private static List<StagePoint> GenerateValidSpots(Agent target)
        {
            List<StagePoint> validPoints = new List<StagePoint>();
            Scene scene = Mission.Current.Scene;
            Vec3 center = target.Position;
            Vec2 center2D = center.AsVec2;

            // 配置：层级参数
            // Layer 1: 半径3米, 8个点
            // Layer 2: 半径5米, 12个点
            // Layer 3: 半径7米, 16个点
            var layers = new[]
            {
                new { Radius = 3.0f, Count = 8, Layer = 1 },
                new { Radius = 4.5f, Count = 12, Layer = 2 },
                new { Radius = 6.0f, Count = 16, Layer = 3 }
            };

            foreach (var layer in layers)
            {
                float angleStep = 360f / layer.Count;

                for (int i = 0; i < layer.Count; i++)
                {
                    float angleDeg = i * angleStep;
                    float angleRad = angleDeg * (MathF.PI / 180f);

                    // 1. 计算理想几何坐标
                    Vec2 dir = new Vec2(MathF.Cos(angleRad), MathF.Sin(angleRad));
                    Vec2 idealPos2D = center2D + dir * layer.Radius;
                    Vec3 idealPos3D = new Vec3(idealPos2D.x, idealPos2D.y, center.z);

                    // 2. 射线检测 (RayCast)：检查从中心到目标点是否有墙壁阻挡
                    // 稍微抬高一点z轴，防止打到地面
                    Vec3 rayStart = center + new Vec3(0, 0, 1.5f);
                    Vec3 rayEnd = idealPos3D + new Vec3(0, 0, 1.5f);

                    float hitDist;
                    BodyFlags excludedFlags = BodyFlags.CommonCollisionExcludeFlags | BodyFlags.Moveable | BodyFlags.AgentOnly;
                    bool hit = scene.RayCastForClosestEntityOrTerrain(rayStart, rayEnd, out hitDist, 0.01f, excludedFlags);

                    // 如果撞到了东西，并且撞击点比目标半径还近 (说明墙在人前面)
                    // 我们做一个简单的“弹性收缩”：如果墙在 4米，我们要 5米，那就把点设在 3.5米
                    float finalRadius = layer.Radius;
                    if (hit && hitDist < layer.Radius)
                    {
                        // 如果障碍物太近（比如小于2米），这个方向就废了，太挤
                        if (hitDist < 2.5f) continue;

                        // 缩短半径，留 0.5m 缓冲
                        finalRadius = hitDist - 0.5f;
                    }

                    // 更新坐标
                    Vec2 finalPos2D = center2D + dir * finalRadius;
                    Vec3 finalPos3D = new Vec3(finalPos2D.x, finalPos2D.y, center.z);

                    // 3. NavMesh 投射 (Project to NavMesh)
                    // 这一步至关重要，确保点位在可走的网格上
                    WorldPosition worldPos = new WorldPosition(scene, finalPos3D);
                    Vec3 navMeshPos = worldPos.GetGroundVec3(); // 先获取地面高度

                    // 修正到导航网格上 (防止点在墙里)
                    WorldPosition targetPos = new WorldPosition(target.Mission.Scene, UIntPtr.Zero, navMeshPos, false);
                    // 如果点无效，尝试获取最近的导航网格
                    if (targetPos.GetNavMesh() == UIntPtr.Zero)
                    {
                        target.Mission.Scene.GetNavigationMeshForPosition(ref navMeshPos);
                        targetPos = new WorldPosition(target.Mission.Scene, navMeshPos);
                    }
                    validPoints.Add(new StagePoint
                    {
                        Position = navMeshPos,
                        LookDirection = (center2D - navMeshPos.AsVec2).Normalized(), // 朝向中心
                        LayerIndex = layer.Layer,
                        IsOccupied = false
                    });
                }
            }

            return validPoints;
        }
        public static void PrecalculateAllocations(Agent target, Agent judge, List<Agent> witnesses)
        {
            // 1. 清理旧缓存
            if (_allocationCache.ContainsKey(target))
            {
                _allocationCache.Remove(target);
            }

            // 2. 生成所有合法的潜在站位 (EQS Generator)
            List<StagePoint> availablePoints = GenerateValidSpots(target);

            // 如果没有生成任何有效点，直接返回
            if (availablePoints.Count == 0) return;

            // 3. 准备分配表
            var assignment = new Dictionary<Agent, StagePoint>();
            _allocationCache[target] = assignment;

            // 4. 优先分配关键人物 (Judge)
            if (judge != null && judge != target && judge.IsActive())
            {
                // 找 LayerIndex 最小（最内圈）且距离 Judge 当前位置最近的点
                var bestSpot = availablePoints
                    .Where(p => !p.IsOccupied)
                    .OrderBy(p => p.LayerIndex) // 优先内圈
                    .ThenBy(p => p.Position.DistanceSquared(judge.Position)) // 其次看移动距离
                    .FirstOrDefault();

                if (bestSpot != null)
                {
                    assignment[judge] = bestSpot;
                    bestSpot.IsOccupied = true;
                }
            }

            List<Agent> unassignedAgents = witnesses
                .Where(w => w != target && (judge == null || w != judge) && w.IsActive())
                .ToList();
            AssignSpotsSimple(unassignedAgents, availablePoints, assignment);
            _allocationCache[target] = assignment;


            if (IsDebugMode)
                Log($"[GroupStage] 分配完成。目标: {target.Name}, 总点位: {availablePoints.Count}, 已分配: {assignment.Count}");
        }
        /// <summary>
        /// 方案A：复杂分配 - 迭代寻找全局最近对
        /// </summary>
        private static void AssignSpotsComplex(List<Agent> agents, List<StagePoint> allPoints, Dictionary<Agent, StagePoint> result)
        {
            // 筛选出所有未占用的点
            var freePoints = allPoints.Where(p => !p.IsOccupied).ToList();
            // 复制一份待分配人员名单
            var pendingAgents = new List<Agent>(agents);

            // 只要还有人没位置，且还有空位
            while (pendingAgents.Count > 0 && freePoints.Count > 0)
            {
                Agent bestAgent = null;
                StagePoint bestPoint = null;
                float minDistanceSq = float.MaxValue;
                int bestAgentIdx = -1;
                int bestPointIdx = -1;

                // 双重循环：寻找当前矩阵中距离最短的一对 (Agent <-> Point)
                for (int i = 0; i < pendingAgents.Count; i++)
                {
                    var agent = pendingAgents[i];
                    for (int j = 0; j < freePoints.Count; j++)
                    {
                        var point = freePoints[j];

                        // 距离权重：优先填满内圈
                        // 如果是 Layer 1，距离视为实际距离；如果是 Layer 2，距离视为实际距离 + 惩罚值
                        // 这样即使外圈的点离我物理上更近，我也倾向于去内圈
                        float distSq = agent.Position.DistanceSquared(point.Position);
                        float layerPenalty = (point.LayerIndex - 1) * 25.0f; // 每一层增加相当于 5米的距离惩罚平方

                        float score = distSq + layerPenalty;

                        if (score < minDistanceSq)
                        {
                            minDistanceSq = score;
                            bestAgent = agent;
                            bestPoint = point;
                            bestAgentIdx = i;
                            bestPointIdx = j;
                        }
                    }
                }

                if (bestAgent != null && bestPoint != null)
                {
                    // 锁定这一对
                    result[bestAgent] = bestPoint;
                    bestPoint.IsOccupied = true; // 标记点位占用

                    // 从待处理列表中移除
                    pendingAgents.RemoveAt(bestAgentIdx);
                    freePoints.RemoveAt(bestPointIdx);
                }
                else
                {
                    break; // 无法找到匹配（理论上不会发生，除非距离无限远）
                }
            }
        }
        /// 方案B：简单分配 - 遍历人找最近点
        /// </summary>
        private static void AssignSpotsSimple(List<Agent> agents, List<StagePoint> allPoints, Dictionary<Agent, StagePoint> result)
        {
            // 为了稍微优化一下效果，我们先把人按照“距离中心”排序
            // 离得近的人先挑位置，这样内部的人不会因为被外部的人抢了位置而跑到最外面
            var sortedAgents = agents.OrderBy(a => a.Position.DistanceSquared(allPoints[0].Position)).ToList();

            foreach (var agent in sortedAgents)
            {
                // 找一个没被占用的、且离这个围观者最近的点
                // 这里加个 LayerIndex 的排序，确保先填满 Layer 1
                var bestSpot = allPoints
                    .Where(p => !p.IsOccupied)
                    .OrderBy(p => p.LayerIndex) // 必须优先填满内圈
                    .ThenBy(p => p.Position.DistanceSquared(agent.Position)) // 同圈内找最近
                    .FirstOrDefault();

                if (bestSpot != null)
                {
                    result[agent] = bestSpot;
                    bestSpot.IsOccupied = true;
                    if(IsDebugMode)
                        DebugLogger.Log($"{agent.Name} 分配到 {bestSpot.Position}");
                }
            }
        }

        private static void Log(string message)
        {
            if (!IsDebugMode) return;
            // 如果你有专门的Logger，请替换这里。这里默认用游戏自带的打印
            string finalMsg = $"[GroupStage] {message}";

            // 方法2: 打印到 Visual Studio 输出窗口 / rgl_log.txt
            DebugLogger.Log(finalMsg);
        }

        public static float CalculateReactionDelay(Agent self,Agent thief,Agent victim)
        {
            // === 1. 计算拟人化反应延迟 (Reaction Latency) ===
            var rand = new Random();
            float delay = 0f;

            // A. 身份基数 (Base Priority)
            if (self.IsHero) delay += 0.0f;           // 英雄/受害者：秒回
            else if (self.Character.IsSoldier) delay += 0.5f;    // 守卫：职业敏感，很快
            else delay += 1.2f;                        // 平民：吃瓜群众，反应最慢

            // B. 距离因子 (Distance Factor)
            float dist = self.Position.Distance(thief.Position);
            delay += dist * 0.25f; // 每远1米增加0.25秒延迟

            // C. 视角因子 (View Angle Factor)
            // 计算点积：我看向的方向 vs 我看向小偷的方向
            Vec2 myLookDir = self.LookDirection.AsVec2.Normalized();
            Vec2 dirToThief = (thief.Position.AsVec2 - self.Position.AsVec2).Normalized();
            float dotProduct = Vec2.DotProduct(myLookDir, dirToThief);
            // dotProduct: 1.0 (正脸), 0 (侧脸), -1.0 (背对)
            // 我们希望背对时延迟最大。映射：1->0s, -1->1s
            float angleFactor = (1.0f - dotProduct) * 1.5f;
            delay += angleFactor;

            // D. 随机扰动 (Random Noise)
            // 加上 0~0.3秒 的随机值，彻底打破同步
            delay += (float)rand.NextDouble() * 2.0f;

            //当事人最先反应
            if (self == victim)
                delay = 0;

            return delay;
        }




    }




}
