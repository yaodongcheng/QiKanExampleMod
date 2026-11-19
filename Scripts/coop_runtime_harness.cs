using System;
using System.Collections.Generic;
using System.Linq;
using LivingWorldNpcs;
using LivingWorldNpcs.Planner.Cooperation;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

// Native motion, raycasts and rendering are doubles. Runtime, router, view,
// planner, director and ledger are the actual production files linked by the runner.
public static class RuntimeTests
{
    static int checks;
    static void Check(bool value, string name)
    { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static CoopContentData content;
    static StoryProbeMissionBehavior host;
    static CoopMissionRuntime runtime;
    static double time;
    static Agent Body(string id, int index, float x)
    {
        var body = new Agent { Index=index, Position=new Vec3(x,0,0), IsMainAgent=id=="player", Name=id };
        AgentAIController.Brains[body]=new AgentBrain { Owner=body, HasDutyPoint=true, DutyPoint=body.Position,
            AmbientOnRelease=id=="blocker"||id=="gate_sentry" };
        host.Actors[id]=body; return body;
    }
    static CoopActivity Manual(string actor, string role, string action)
    {
        var a=new CoopActivity { Id="manual-" + Guid.NewGuid(), RunId="test-run", ActorId=actor, RoleId=role,
            Content=content, Domain=content.Domain, State=CoopActivityState.Executing, StepRevision=1,
            Plan=new CoopPlan(), InteractionPartnerId="son", FocusTargetId="gate_sentry" };
        a.Plan.Steps.Add(action);
        foreach(var fact in new[]{"alive","briefed","companion_ready","talk_authorized","lure_heard","guide_departed","engaged","guided"})
            a.Facts.Set(fact,CoopTruth.True,"test",time);
        a.Facts.Set("cancelled",CoopTruth.False,"test",time);
        a.Facts.Set("single_guard",CoopTruth.False,"test",time);
        foreach(var id in content.RoleOrNull("guard").Actors)
        {
            a.Facts.Set("present_"+id,CoopTruth.True,"test",time);
            a.Facts.Set("handled_"+id,CoopTruth.True,"test",time);
        }
        return a;
    }
    static void Reset()
    {
        CoopState.ResetAll();
        CoopDirector.Install(content,null,1); CoopRequestRouter.Reset(); CoopView.Reset(); SpeechChannel.Queue.Clear();
        PerformanceAction.Leases.Clear(); AgentAIController.Brains.Clear();
        host=new StoryProbeMissionBehavior(); StoryProbeMissionBehavior.Current=host;
        Body("son",1,20); Body("blocker",2,1); Body("gate_sentry",3,2); Agent.Main=Body("player",0,3);
        runtime=new CoopMissionRuntime(host); CoopMissionBehavior.Current=new CoopMissionBehavior(runtime);
        CoopTestSession.Rolls.Clear(); NpcSightSystem.Visible=true; ImChatView.AllowShow=true; ImChatView.Shows=0; time=0;
        NearbyFeed.Messages.Clear();InformationManager.Messages.Clear();SpeechChannel.Shown.Clear();SpeechChannel.Times.Clear();
    }
    static void Tick(double seconds=.1) { time+=seconds; runtime.Tick((float)seconds,time); }
    static void DialogueTick(double seconds=.1)
    { Tick(seconds);CoopRequestRouter.Tick(time);SpeechChannel.Flush(); }
    static void FinishDialogue(Func<bool> done)
    { for(int frame=0;frame<400&&!done();frame++)DialogueTick(); }
    static CoopActivity StartDialogue(out Func<CoopStepOutcome?> outcome)
    {
        Reset();host.Actors["son"].Position=new Vec3(3,0,0);
        var activity=Manual("son","companion","lure_request_blocker");CoopStepOutcome? result=null;
        CoopTestSession.Rolls.Enqueue(1);
        CoopRequestRouter.Submit(runtime,activity,content.Domain.Actions["lure_request_blocker"],host.Actors["son"],host.Actors["blocker"],o=>result=o);
        outcome=()=>result;return activity;
    }
    static CoopActivity ExecuteGroup(CoopAlgorithm algorithm, bool secondRefuses)
    {
        Reset();host.Actors["son"].Position=new Vec3(3,0,0);
        CoopRecruitment.UseTemporary=true;CoopRecruitment.FavorDebits=0;
        var director=CoopDirector.Current;director.Runtime=runtime;
        var facts=Manual("son","companion","lure_request_blocker").Facts.Snapshot();
        foreach(var fact in new[]{"guided","engaged","signal","passed","regrouped","player_crossed","gap",
            "other_watcher","near_guard","near_blocker","near_gate_sentry","handled_blocker","handled_gate_sentry"})
            facts[fact]=CoopTruth.False;
        var root=director.StartSeeded("son","companion",content.GoalOrNull("help_player_cross"),algorithm,
            "integration",time,facts,out var why);root.PlayerInformed=true;
        CoopTestSession.Rolls.Enqueue(1);CoopTestSession.Rolls.Enqueue(secondRefuses?0:1);
        double? firstSignal=null;
        bool heldBeforeCross=false;
        bool heldAtLure=true;
        for(int frame=0;frame<1800;frame++)
        {
            // Await only pure background planning. Native motion and delivery are explicit doubles below.
            foreach(var activity in director.Activities.ToArray())activity.PendingSolve?.GetAwaiter().GetResult();
            time+=.1;director.Tick(.1,time);runtime.Tick(.1f,time);CoopRequestRouter.Tick(time);SpeechChannel.Flush();
            foreach(var pair in PerformanceAction.Leases.ToArray())
            {
                var lease=pair.Value;
                if(lease.CanControl && !lease.Ended && lease.IsWalking)
                {
                    // 以有限速度推进，保留守卫实际看到/听到引路者离岗的距离窗口。
                    var target=lease.FollowTarget!=null?lease.FollowTarget.Position:lease.WalkTarget;
                    var position=pair.Key.Position;var distance=position.Distance(target);
                    if(distance<=lease.StopDistance+.01f){lease.IsWalking=false;lease.WalkResult=true;}
                    else
                    {
                        float fraction=Math.Min(.3f,distance-lease.StopDistance)/distance;
                        pair.Key.Position=new Vec3(position.x+(target.x-position.x)*fraction,
                            position.y+(target.y-position.y)*fraction,position.z+(target.z-position.z)*fraction);
                    }
                }
            }
            if(root.Facts.IsTrue("signal"))
            {
                if(firstSignal==null)firstSignal=time;
                heldAtLure &= host.Actors["son"].Position.Distance(new Vec3(20,0,0))<=1.01f;
                if(time-firstSignal<5)heldBeforeCross|=!root.Facts.IsTrue("passed") && root.State!=CoopActivityState.Success;
                else {host.PlayerHasCrossed=true;Agent.Main.Position=new Vec3(8,0,0);}
            }
            if(root.IsTerminal && (!secondRefuses || director.ForActor("son")?.IsTerminal==true))break;
        }
        if(!secondRefuses)
        {
            if(!heldBeforeCross || root.State!=CoopActivityState.Success)Console.WriteLine("GROUP_DIAG "+root.State+" "+root.LastReason+" steps="+string.Join(",",root.Plan?.Steps??new List<string>())+" history="+string.Join(";",root.History)+"\n"+string.Join("\n",CoopLog.Recent().TakeLast(40)));
            Check(heldBeforeCross,"full group execution waits for actual player crossing after signal");
            Check(root.State==CoopActivityState.Success && root.Facts.IsTrue("passed") && !root.Facts.IsTrue("regrouped"),
                "actual director runtime router complete the two-guard chain");
            Check(heldAtLure && !root.Plan.Steps.Contains("regroup"),
                "helper stays at lure point through crossing without following player");
            Check(director.Ledger.Attempts.Count==2,"full group execution resolves each guard exactly once");
        }
        else
        {
            Check(root.State==CoopActivityState.Failed && !root.Facts.IsTrue("signal") && !root.Facts.IsTrue("guided"),
                "second refusal prevents leading and signalling throughout full execution");
            Check(director.ForActor("son")?.RoleId=="reporter" && director.ForActor("son").State==CoopActivityState.Success,
                "full refusal chain completes separate failure report and return");
        }
        Check(CoopRecruitment.FavorDebits==1 && root.FavorCommitted,
            "actual router charges one activity across both guards and refusal");
        CoopRecruitment.UseTemporary=false;
        return root;
    }
    static void HandoffRegression()
    {
        Reset();var guard=host.Actors["blocker"];
        var old=PerformanceAction.TryAcquire(guard,"old-reply");
        old.Release("reply_finished");
        Check(AgentAIController.Brains[guard].AmbientInsertions==1 && PerformanceAction.TryAcquire(guard,"new-follow")==null,
            "real Brain arbitration reproduces release then ambient turn refusal");

        var sender=StartDialogue(out var response);CoopDirector.Current.Runtime=runtime;
        guard=host.Actors["blocker"];var reply=PerformanceAction.Leases[guard];
        FinishDialogue(()=>response()!=null);
        var receiver=CoopDirector.Current.ForActor("blocker");
        Check(response()?.Kind==CoopStepOutcomeKind.Completed && receiver!=null && runtime.HasControl(receiver)
            && ReferenceEquals(reply,PerformanceAction.Leases[guard]) && !reply.Ended
            && AgentAIController.Brains[guard].AmbientInsertions==0,
            "accepted reply hands the same live lease to receiver without a release gap");
        CoopRequestRouter.Cancel(sender.Id,"late_cancel");SpeechChannel.Flush();
        Check(reply.Controls(guard),"old pending cancellation and speech cannot release transferred lease");
        Check(CoopLog.Recent().Any(line=>line.Contains("brain.handoff")&&line.Contains(reply.OwnerToken)),
            "handoff logs original lease owner and receiving activity");
        Check(!runtime.AdoptReplyLease(receiver,reply,sender.Id),"duplicate handoff rejected");
        var duplicate=Manual("blocker","guard","wait_guide");
        Check(!runtime.AdoptReplyLease(duplicate,reply,sender.Id),"same lease cannot back two activities");
        var foreign=Manual("gate_sentry","guard","wait_guide");
        Check(!runtime.AdoptReplyLease(foreign,reply,sender.Id),"foreign actor cannot adopt a reply lease");
        runtime.Pause(receiver,true,time);receiver.State=CoopActivityState.Paused;
        Check(!reply.CanControl && !reply.Ended,"pause keeps handoff lease but returns body control");
        receiver.State=CoopActivityState.Requested;runtime.Pause(receiver,false,time);
        Check(reply.Controls(guard),"resume retains receiver ownership");
        receiver.PendingSolve?.GetAwaiter().GetResult();CoopDirector.Current.Tick(.1,time+.1);Tick();
        Check(receiver.IsActive && reply.Controls(guard) && receiver.CurrentAction(receiver.Domain)?.Id=="wait_guide",
            "receiver starts waiting with original reply lease despite ambient competitor");
        CoopDirector.Current.StopActivity(receiver,"scene_exit",time);
        Check(reply.Ended && !runtime.HasControl(receiver),"scene exit releases transferred lease");
        Check(!runtime.AdoptReplyLease(duplicate,reply,sender.Id),"ended reply lease cannot be adopted");

        foreach(var lost in new[]{"bound","combat","dead","mission"})
        {
            Reset();guard=host.Actors["blocker"];reply=PerformanceAction.TryAcquire(guard,"reply-loss");
            duplicate=Manual("blocker","guard","wait_guide");
            if(lost=="bound")AgentAIController.Brains[guard].IsBound=true;
            if(lost=="combat")AgentAIController.Brains[guard].IsInCombat=true;
            if(lost=="dead")guard.Health=0;
            if(lost=="mission")Mission.Current=new Mission();
            Check(!runtime.AdoptReplyLease(duplicate,reply,"sender"),"handoff refuses expired actor state "+lost);
        }
        sender=StartDialogue(out response);CoopDirector.Current.Runtime=runtime;
        sender.Domain=new CoopDomain();reply=PerformanceAction.Leases[host.Actors["blocker"]];
        FinishDialogue(()=>response()!=null);receiver=CoopDirector.Current.ForActor("blocker");
        receiver.PendingSolve?.GetAwaiter().GetResult();CoopDirector.Current.Tick(.1,time+.1);
        Check(receiver.State==CoopActivityState.Failed && reply.Ended && !runtime.HasControl(receiver),
            "receiver solve failure releases transferred lease");
        sender=StartDialogue(out response);CoopDirector.Current.Runtime=runtime;
        FinishDialogue(()=>response()!=null);receiver=CoopDirector.Current.ForActor("blocker");
        reply=PerformanceAction.Leases[host.Actors["blocker"]];sender.State=CoopActivityState.Cancelled;
        runtime.Stop(sender,"player_cancelled");SpeechChannel.Flush();
        Check(receiver.State==CoopActivityState.Cancelled && reply.Ended && !runtime.HasControl(receiver),
            "sender terminal cancellation releases only its transferred invitation");
        Reset();
    }
    public static void Main(string[] args)
    {
        content=CoopContentData.Load(args[0]); Check(content.IsValid,"runtime content valid"); Reset();
        var moduleData=System.IO.Directory.GetParent(System.IO.Directory.GetParent(args[0]).FullName).Parent.FullName;
        foreach(var node in System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(moduleData,"Languages","CNs","std_LivingWorldNpcs_strings.xml")).Descendants("string"))
            LWNTextHelper.Texts[(string)node.Attribute("id")]=(string)node.Attribute("text");
        var source=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(args[0]));
        foreach(var p in ((Newtonsoft.Json.Linq.JObject)source["Texts"]).Properties())
            LWNTextHelper.Texts[p.Name]=(string)p.Value["zh"];
        HandoffRegression();
        var follower=host.Actors["blocker"];var guide=host.Actors["son"];
        guide.Position=new Vec3(5,0,0);
        var probe=new NativeFollowLease(AgentAIController.Brains[follower]);
        Check(probe.BeginFollow(guide,2,keepFollowing:true),"actual lease starts persistent native follow");
        for(int frame=0;frame<200;frame++)
        {guide.Position=new Vec3(20+frame*.1f,0,0);follower.Position=new Vec3(guide.Position.x-4,0,0);probe.Walking.OnTick(follower,.1f);}
        Check(!probe.Walking.IsFinished(follower),"actual native follow survives twenty seconds with constant separation");
        guide.Active=false;Check(probe.Walking.IsFinished(follower),"persistent native follow still ends on guide loss");
        Reset();follower=host.Actors["blocker"];guide=host.Actors["son"];probe=new NativeFollowLease(AgentAIController.Brains[follower]);
        guide.Position=new Vec3(5,0,0);
        probe.BeginFollow(guide,2);
        for(int frame=0;frame<80;frame++)
        {guide.Position=new Vec3(5+frame*.1f,0,0);follower.Position=new Vec3(guide.Position.x-4,0,0);probe.Walking.OnTick(follower,.1f);}
        Check(probe.Walking.IsFinished(follower),"actual approach default reproduces constant-gap timeout and remains unchanged");
        Reset();
        var a=Manual("blocker","guard","follow_guide"); CoopStepOutcome? result=null;
        runtime.Start(a,content.Domain.Actions["follow_guide"],time,o=>result=o); Tick();
        var lease=PerformanceAction.Leases[host.Actors["blocker"]];
        Check(result==null && lease.Follows==1,"follow waits for real movement");
        Check(lease.KeepFollowing,"group follow requests persistent movement");
        lease.IsWalking=false; lease.WalkResult=true; Tick();
        Check(result==null,"nominal successful walk cannot finish at wrong position");
        host.Actors["blocker"].Position=new Vec3(20,0,0); Tick();
        Check(result?.Kind==CoopStepOutcomeKind.Completed,"guard and guide both at destination finish follow");
        runtime.Stop(a,"test_cleanup");
        Reset(); a=Manual("blocker","guard","follow_guide");result=null;
        runtime.Start(a,content.Domain.Actions["follow_guide"],time,o=>result=o);Tick();
        AgentAIController.Brains[host.Actors["son"]].IsBound=true;Tick();
        Check(result?.Kind==CoopStepOutcomeKind.Failed && result?.Reason=="guide_lost","bound guide invalidates following");
        Reset();a=Manual("blocker","guard","follow_guide");result=null;
        runtime.Start(a,content.Domain.Actions["follow_guide"],time,o=>result=o);Tick();
        lease=PerformanceAction.Leases[host.Actors["blocker"]];lease.IsWalking=false;lease.WalkResult=false;Tick();
        Check(result?.Reason=="follow_incomplete","blocked follow reports failure");
        Reset();a=Manual("blocker","guard","follow_guide");result=null;
        runtime.Start(a,content.Domain.Actions["follow_guide"],time,o=>result=o);Tick();
        runtime.Pause(a,true,time);a.State=CoopActivityState.Paused;Tick(10);
        Check(result==null,"paused follow cannot finish");
        a.State=CoopActivityState.Executing;runtime.Pause(a,false,time);Tick();
        Check(PerformanceAction.Leases[host.Actors["blocker"]].Follows==2,"resumed follow reacquires a real follow action");

        Reset(); a=Manual("son","companion","observe_lured_gate"); result=null;
        runtime.Start(a,content.Domain.Actions["observe_lured_gate"],time,o=>result=o);Tick();Tick();
        Check(result==null && !a.Facts.IsTrue("gap"),"clear-sight query alone cannot signal while guards remain at post");
        host.Actors["blocker"].Position=new Vec3(19,0,0);host.Actors["gate_sentry"].Position=new Vec3(18,0,0);Tick();
        Check(result?.Kind==CoopStepOutcomeKind.Completed && a.Facts.IsTrue("gap"),"real participant arrival and sight permit observation");
        host.Actors["gate_sentry"].Position=new Vec3(2,0,0);runtime.Refresh(a,time);
        Check(!a.Facts.IsTrue("gap") && !a.Facts.IsTrue("signal"),"returning guard revokes the opening");

        Reset();a=Manual("son","companion","signal_player");result=null;
        host.Actors["blocker"].Position=new Vec3(19,0,0);host.Actors["gate_sentry"].Position=new Vec3(18,0,0);
        NpcSightSystem.PlayerVisible=false;
        runtime.Start(a,content.Domain.Actions["signal_player"],time,o=>result=o);Tick();SpeechChannel.Flush();Tick();
        Check(result==null && PerformanceAction.Leases[host.Actors["son"]].Actions==1,"unseen gesture cannot advance signal");
        runtime.Stop(a,"player_cancelled");SpeechChannel.Flush();Tick();
        Check(result==null,"cancelled queued signal cannot call its old receipt");NpcSightSystem.PlayerVisible=true;
        Reset();a=Manual("son","companion","signal_player");result=null;
        host.Actors["blocker"].Position=new Vec3(19,0,0);host.Actors["gate_sentry"].Position=new Vec3(18,0,0);
        runtime.Start(a,content.Domain.Actions["signal_player"],time,o=>result=o);Tick();SpeechChannel.Flush();Tick();
        Check(result?.Kind==CoopStepOutcomeKind.Completed && !CoopMissionRuntime.CanHear(Agent.Main,host.Actors["son"]),
            "visible gesture works outside ordinary hearing distance");
        Check(NearbyFeed.Messages.Count==1 && InformationManager.Messages.Count==1,"witnessed signal creates separate nearby message and immediate notice");
        Tick();SpeechChannel.Flush();Check(NearbyFeed.Messages.Count==1,"signal receipt is delivered once");
        Reset();a=Manual("son","companion","signal_player");result=null;
        host.Actors["blocker"].Position=new Vec3(19,0,0);host.Actors["gate_sentry"].Position=new Vec3(18,0,0);
        Agent.Main.Position=new Vec3(-20,0,0);
        runtime.Start(a,content.Domain.Actions["signal_player"],time,o=>result=o);Tick();SpeechChannel.Flush();Tick();
        Check(result==null && !CoopMissionRuntime.CanSeeWithin(Agent.Main,host.Actors["son"],30),
            "player sight ignoring radius cannot deliver a distant gesture");
        Check(NearbyFeed.Messages.Count==0 && InformationManager.Messages.Count==0,"unseen gesture creates no remote notice");

        Reset();a=Manual("son","companion","lure_request_blocker");CoopTestSession.Rolls.Enqueue(1);result=null;
        CoopRequestRouter.Submit(runtime,a,content.Domain.Actions["lure_request_blocker"],host.Actors["son"],host.Actors["blocker"],o=>result=o);
        Check(result?.Reason=="request_not_heard" && CoopDirector.Current.Ledger.Attempts.Count==0,"out-of-range request does not roll");
        host.Actors["son"].Position=new Vec3(3,0,0);result=null;
        CoopRequestRouter.Submit(runtime,a,content.Domain.Actions["lure_request_blocker"],host.Actors["son"],host.Actors["blocker"],o=>result=o);
        DialogueTick();Check(result==null && CoopDirector.Current.Ledger.Attempts.Count==0,"challenge precedes roll and guard dispatch");
        var reloaded=CoopContentData.Load(args[0]);reloaded.ContentVersion="runtime-reloaded";
        CoopDirector.Current.Reload(reloaded,null);FinishDialogue(()=>result!=null);
        Check(result?.Kind==CoopStepOutcomeKind.Completed && CoopDirector.Current.ForActor("blocker")?.RoleId=="guard",
            "heard accepted answer starts guard's own plan");
        var firstGuard=CoopDirector.Current.ForActor("blocker");
        Check(ReferenceEquals(firstGuard.Content,a.Content) && ReferenceEquals(firstGuard.Domain,a.Domain),
            "receiver keeps sender content and domain across reload");
        runtime.Stop(a,"replan");
        Check(firstGuard.IsActive,"finite sender replan does not cancel accepted invitation");
        CoopTestSession.Rolls.Enqueue(0);result=null;
        CoopRequestRouter.Submit(runtime,a,content.Domain.Actions["lure_request_gate_sentry"],host.Actors["son"],host.Actors["gate_sentry"],o=>result=o);
        FinishDialogue(()=>result!=null);
        Check(result?.Reason=="refused" && CoopDirector.Current.ForActor("gate_sentry")==null,"second guard refusal creates no follow plan");
        var unrelated=CoopMissionBehavior.Current.StartRoleActivity("gate_sentry",reloaded.RoleOrNull("guard"),
            reloaded.GoalOrNull("guard_follow"),firstGuard.Facts.Snapshot(),"son","another_request",out var unrelatedReason);
        Check(unrelated!=null,"unrelated guard invitation prepared");
        unrelated.RequestActivityId="another-activity";
        a.State=CoopActivityState.Failed;runtime.Stop(a,"no_alternative");
        Check(firstGuard.State==CoopActivityState.Cancelled && CoopMissionBehavior.Current.Reports==1,
            "terminal sender failure cancels invitation and starts separate report");
        Check(CoopDirector.Current.ForActor("son")?.RoleId=="reporter" && a.State==CoopActivityState.Failed,
            "recovery is not original task success");
        Check(unrelated.IsActive,"terminal sender cleanup preserves unrelated guard invitation");
        Check(ReferenceEquals(CoopDirector.Current.ForActor("son").Content,a.Content),
            "failure recovery retains original content across reload");
        Reset();host.Actors["son"].Position=new Vec3(3,0,0);a=Manual("son","companion","lure_request_blocker");result=null;
        CoopTestSession.Rolls.Enqueue(1);
        CoopRequestRouter.Submit(runtime,a,content.Domain.Actions["lure_request_blocker"],host.Actors["son"],host.Actors["blocker"],o=>result=o);
        DialogueTick();host.Actors["son"].Position=new Vec3(20,0,0);DialogueTick();
        Check(result?.Reason=="request_not_heard" && CoopDirector.Current.ForActor("blocker")==null,"unheard dialogue never starts guard activity");
        Reset();host.Actors["son"].Position=new Vec3(3,0,0);a=Manual("son","companion","lure_request_blocker");result=null;
        CoopTestSession.Rolls.Enqueue(1);
        CoopRequestRouter.Submit(runtime,a,content.Domain.Actions["lure_request_blocker"],host.Actors["son"],host.Actors["blocker"],o=>result=o);
        DialogueTick();CoopRequestRouter.Cancel(a.Id,"cancelled");SpeechChannel.Flush();
        Check(result==null && CoopDirector.Current.ForActor("blocker")==null,"cancelled answer cannot resurrect receiver plan");

        a=StartDialogue(out var response);FinishDialogue(()=>SpeechChannel.Shown.Count==3);
        Check(response()==null && CoopDirector.Current.Ledger.Attempts.Count==1 && CoopDirector.Current.ForActor("blocker")==null,
            "final acceptance appearing does not dispatch guard before it is read");
        Check(SpeechChannel.Shown.SequenceEqual(new[]{LWNTextHelper.Texts["LWN_coop_line_challenge_blocker"],
            LWNTextHelper.Texts["LWN_coop_line_clarify_blocker"],LWNTextHelper.Texts["LWN_coop_line_accept_blocker"]}),
            "guard duty objection and clarification precede conditional acceptance");
        Check(Enumerable.Range(1,2).All(i=>SpeechChannel.Times[i]-SpeechChannel.Times[i-1]>=
            Math.Max(3,SpeechChannel.EstimateDuration(SpeechChannel.Shown[i-1]))-.001),"cross-speaker turns wait full reading duration at least three seconds");
        a.State=CoopActivityState.Paused;CoopRequestRouter.Pause(a.Id,true);DialogueTick(10);
        Check(response()==null && CoopDirector.Current.ForActor("blocker")==null,"pause during final answer cannot dispatch guard");
        a.State=CoopActivityState.Executing;CoopRequestRouter.Pause(a.Id,false);DialogueTick();
        Check(response()==null && CoopDirector.Current.Ledger.Attempts.Count==1,"resumed answer restarts reading without a second roll");
        FinishDialogue(()=>response()!=null);
        Check(response()?.Kind==CoopStepOutcomeKind.Completed && CoopDirector.Current.Ledger.Attempts.Count==1,
            "resumed answer commits once after full reading");
        a=StartDialogue(out response);FinishDialogue(()=>SpeechChannel.Shown.Count==3);
        host.Actors["son"].Position=new Vec3(20,0,0);DialogueTick();
        Check(response()?.Reason=="reply_not_heard" && CoopDirector.Current.ForActor("blocker")==null,"leaving during final reply cannot commit guard movement");
        a=StartDialogue(out response);FinishDialogue(()=>SpeechChannel.Shown.Count==3);
        AgentAIController.Brains[host.Actors["blocker"]].IsBound=true;DialogueTick();
        Check(response()?.Kind==CoopStepOutcomeKind.Failed && CoopDirector.Current.ForActor("blocker")==null,"guard incapacitated during final answer cannot depart");
        a=StartDialogue(out response);FinishDialogue(()=>SpeechChannel.Shown.Count==3);
        CoopRequestRouter.Cancel(a.Id,"cancelled");DialogueTick(10);
        Check(response()==null && CoopDirector.Current.ForActor("blocker")==null,"cancellation during final reading cannot resurrect guard activity");

        Reset();host.Actors["son"].Position=new Vec3(3,0,0);a=Manual("son","companion","lure_request_blocker");result=null;
        a.Facts.Set("handled_blocker",CoopTruth.False,"test",time);
        runtime.Start(a,content.Domain.Actions["lure_request_blocker"],time,o=>result=o);DialogueTick();
        double shownAt=time;double reading=Math.Max(3,SpeechChannel.EstimateDuration(SpeechChannel.Shown[0]));
        DialogueTick(reading-.2);
        Check(SpeechChannel.Shown.Count==1 && CoopDirector.Current.Ledger.Attempts.Count==0,"initiator opening is read before guard challenges");
        FinishDialogue(()=>result!=null);
        Check(SpeechChannel.Shown.Count==4 && SpeechChannel.Times[1]-shownAt>=reading-.001,"actual runtime and router execute four separated dialogue turns");

        Reset();host.Actors["son"].Position=new Vec3(3,0,0);
        a=Manual("son","companion","lure_request_blocker");a.AwaitingApproval=true;
        // Use the actual director to register a reachable preview for view/approval tests.
        var facts=a.Facts.Snapshot();facts["near_blocker"]=CoopTruth.True;facts["near_gate_sentry"]=CoopTruth.True;
        facts["handled_blocker"]=CoopTruth.False;facts["handled_gate_sentry"]=CoopTruth.False;
        foreach(var fact in new[]{"signal","passed","regrouped","player_crossed","gap","other_watcher","near_guard"}) facts[fact]=CoopTruth.False;
        a=CoopDirector.Current.StartSeeded("son","companion",content.GoalOrNull("help_player_cross"),CoopAlgorithm.Htn,"preview",time,facts,out var why);
        Check(a!=null,"preview plan registered");a.AwaitingApproval=true;ImChatView.AllowShow=false;
        CoopView.Tick(CoopDirector.Current,time);Check(!a.PlayerInformed,"queued card does not count as seen");
        Check(!CooperationGateTests.CanOpen() && CooperationGateTests.CanOpen(localCooperation:true),
            "no-LLM local card opens while ordinary IM remains gated");
        Settings.Instance.InteractionDisabled=true;
        Check(!CooperationGateTests.CanOpen(localCooperation:true),"local card retains interaction/body gate");
        Settings.Instance.InteractionDisabled=false;ConversationEntryPatch.Active=true;
        Check(!CooperationGateTests.CanOpen(localCooperation:true),"local card cannot cover formal dialogue");
        ConversationEntryPatch.Active=false;UiFullScreenHelper.FullScreen=true;
        Check(!CooperationGateTests.CanOpen(localCooperation:true),"local card cannot cover full-screen UI");
        UiFullScreenHelper.FullScreen=false;Mission.Current=null;
        Check(!CooperationGateTests.CanOpen(localCooperation:true),"local exception cannot open IM outside a mission");
        Mission.Current=new Mission();
        ImChatView.AllowShow=true;CoopView.Tick(CoopDirector.Current,time);
        Check(a.PlayerInformed && ImChatView.Shows>0,"actual display acknowledgement unlocks approval");
        Check(CoopDirector.Current.Approve(a.Id,a.PlanRevision,time) && !a.AwaitingApproval,"received current preview can start");
        Check(CoopView.BuildBasic(a,content).Any(s=>s.Contains("blocker")) && CoopView.BuildBasic(a,content).Any(s=>s.Contains("gate_sentry")),
            "card lists both actual participants");
        var completedCard=Manual("son","companion","wait_player_cross");completedCard.State=CoopActivityState.Success;
        Check(CoopView.BuildBasic(a,content).Contains(LWNTextHelper.Texts["LWN_coop_plan_rendezvous"])
            && LWNTextHelper.Texts["LWN_coop_plan_rendezvous"].Contains("不用等我再走到你身边")
            && CoopView.BuildDetail(completedCard,content).Contains(LWNTextHelper.Texts["LWN_coop_result_success"]),
            "plan and success card describe crossing without forced rendezvous");
        foreach(var method in new[]{("talk_guard","LWN_coop_plan_talk_method"),("request_investigation","LWN_coop_plan_investigate_method")})
        {
            var alternative=Manual("son","companion",method.Item1);var text=CoopView.BuildBasic(alternative,content);
            Check(text.Contains(LWNTextHelper.Texts[method.Item2]) && !text.Contains(LWNTextHelper.Texts["LWN_coop_plan_lure_method"]),
                "briefing describes actual alternative tactic "+method.Item1);
        }
        foreach(var algorithm in new[]{CoopAlgorithm.Goap,CoopAlgorithm.Htn,CoopAlgorithm.Hybrid})
        {
            Console.WriteLine("GROUP_ALGORITHM "+algorithm);
            ExecuteGroup(algorithm,false);ExecuteGroup(algorithm,true);
        }
        Console.WriteLine("RUNTIME_PASS checks="+checks);
    }
}

namespace TaleWorlds.Library
{
    public struct Vec3
    {
        public float x,y,z;public Vec3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vec3 Zero=>new Vec3();public Vec3 AsVec2=>this;
        public float Distance(Vec3 v)=>(float)Math.Sqrt((x-v.x)*(x-v.x)+(y-v.y)*(y-v.y)+(z-v.z)*(z-v.z));
        public float DistanceSquared(Vec3 v){var d=Distance(v);return d*d;}
        public Vec3 NormalizedCopy()=>this;
        public void Normalize(){}public static Vec3 operator+(Vec3 a,Vec3 b)=>new Vec3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vec3 operator-(Vec3 a,Vec3 b)=>new Vec3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vec3 operator*(Vec3 a,float n)=>new Vec3(a.x*n,a.y*n,a.z*n);
        public static bool operator==(Vec3 a,Vec3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        public static bool operator!=(Vec3 a,Vec3 b)=>!(a==b);public override bool Equals(object v)=>v is Vec3 b&&this==b;
        public override int GetHashCode()=>x.GetHashCode();
    }
    public class InformationMessage {public string Text;public InformationMessage(string text){Text=text;}}
    public static class InformationManager {public static List<string> Messages=new List<string>();public static void DisplayMessage(InformationMessage m){Messages.Add(m.Text);}}
}
namespace TaleWorlds.Core { public class SkillObject { public string StringId="charm"; } }
namespace TaleWorlds.ObjectSystem
{
    public class MBObjectManager
    {
        public static MBObjectManager Instance=new MBObjectManager();public T GetObject<T>(string id) where T:new()=>new T();
        public T GetObject<T>(Func<T,bool> predicate) where T:new()=>new T();
    }
}
namespace TaleWorlds.MountAndBlade
{
    public class Mission {public static Mission Current=new Mission();}
    public class Agent
    {
        public static Agent Main;public int Index;public float Health=100;public bool Active=true,IsMainAgent;public string Name;
        public Vec3 Position;public FrameValue Frame=new FrameValue();public CharacterValue Character=new CharacterValue();
        public bool IsUsingGameObject,CrouchMode;public void SetLookAgent(Agent a){}public void ClearTargetFrame(){}
        public void TeleportToPosition(Vec3 p){Position=p;}public void SetMovementDirection(Vec3 d){}
    }
    public class FrameValue { public RotationValue rotation=new RotationValue(); }
    public class RotationValue { public Vec3 f=new Vec3(1,0,0); }
    public class CharacterValue { public int GetSkillValue(TaleWorlds.Core.SkillObject skill)=>50; }
}
namespace LivingWorldNpcs
{
    public class AgentBrain
    {
        public bool IsBound,IsStatusHeld,IsStunned,IsInCombat,HasDutyPoint;public Vec3 DutyPoint;public Agent Owner;
        public static bool IsRoped(Agent a)=>false;public static bool IsKnockedOut(Agent a)=>false;
        private PerformanceAction _performance;private IAtomicAction _currentAction;
        private Queue<IAtomicAction> _actionQueue=new Queue<IAtomicAction>();
        public bool IsStoryIntercepting,AmbientOnRelease=true;public int AmbientInsertions;
        public NpcIntent CurrentIntent=new NpcIntent();
        public bool IsPerforming=>_performance!=null&&!_performance.Ended;
        internal bool OwnsPerformance(PerformanceAction lease)=>_performance==lease;
        private IAtomicAction EffectiveAction=>_currentAction;
        // 原生逐帧调度替身：租约正常结束后模拟日常系统先插入转身，仲裁用真实方法。
        private void ClearAllActions(bool lockPlace)
        {bool released=_currentAction is PerformanceAction p&&p.Ended;_currentAction=null;_actionQueue.Clear();
         if(released&&AmbientOnRelease){_currentAction=new TurnToDirectionAction();AmbientInsertions++;}}
        private void EnqueueAction(PerformanceAction lease){_currentAction=lease;lease.Started=true;PerformanceAction.Leases[Owner]=lease;}
        public void Receive(AIEvent ev)=>HandlePerformanceEvent(ev);
        /* ACTUAL_PERFORMANCE_ARBITRATION */
    }
    public class AIEvent {public string EventType;public object[] Args;}
    public enum NpcIntentType {None,ExecutingCommand,Fighting,Confronting}
    public class NpcIntent {public NpcIntentType Type;}
    public class StayAction:IAtomicAction {public bool IsKnockout;}
    public class StreetIdleAction:IAtomicAction { }
    public class TurnToDirectionAction:IAtomicAction { }
    public static class AgentAIController
    { public static AgentAIControllerBridge Instance=new AgentAIControllerBridge();
      public static Dictionary<Agent,AgentBrain> Brains=new Dictionary<Agent,AgentBrain>();public static AgentBrain GetBrainForAgent(Agent a)=>a!=null&&Brains.TryGetValue(a,out var b)?b:null; }
    public class AgentAIControllerBridge
    {public void SendEventToAgent(Agent a,string type,params object[] args)=>AgentAIController.GetBrainForAgent(a)?.Receive(new AIEvent{EventType=type,Args=args});}
    public static class AgentControlHelper
    { public static bool SafeIsActive(Agent a)=>a?.Active==true;public static bool MovePrepare(Agent a)=>true;
      public static void ForceUnlockAgent(Agent a){}public static void MoveEndAndInteractPrepare(Agent a){}public static void SetAgentPosition(Agent a,Vec3 p){a.Position=p;} }
    public interface IAtomicAction { }
    public class MoveToPositionAction {public enum EndBehavior {InteractPrepare,Unlock}}
    /* ACTUAL_FOLLOW_ACTION */
    public class NativeFollowLease
    {
        private FollowAgentAction _walking;private AgentBrain _brain;private NativePresentation _presentation=new NativePresentation();
        private Agent _walkTarget;private float _followStop;private bool _bodyHeld;
        public bool CanControl=true;public bool? WalkResult;public FollowAgentAction Walking=>_walking;
        public bool BodyHeld=>_bodyHeld;
        public NativeFollowLease(AgentBrain brain){_brain=brain;}
        /* ACTUAL_BEGIN_FOLLOW */
    }
    public class NativePresentation {public void Stop(){}}
    public static class CompanionDetentionBehavior { public static bool IsDetained(Agent a)=>false; }
    public static class NpcSightSystem
    {
        public static bool Visible=true,PlayerVisible=true;
        public static bool CanAgentSeeTarget(Agent a,Agent b,float radius=15)=>a!=null&&b!=null&&Visible&&(a!=Agent.Main||PlayerVisible)&&(a==Agent.Main||a.Position.Distance(b.Position)<=radius);
        public static bool IsTargetInViewCone(Agent a,Agent b,float angle,bool useBodyFacing=false)=>Visible;
    }
    public class PerformanceAction:IAtomicAction
    {
        public static Dictionary<Agent,PerformanceAction> Leases=new Dictionary<Agent,PerformanceAction>();
        private readonly AgentBrain _brain;private readonly Mission _mission;private bool _ended;
        public string OwnerToken {get;}public bool Started,Paused;public bool Ended=>_ended;
        /* ACTUAL_PERFORMANCE_CONTROL */
        /* ACTUAL_CONTROLS_ACTOR */
        public bool IsWalking,PauseOnConversation;public bool? WalkResult;public int Follows,Actions;
        public Agent FollowTarget;public Vec3 WalkTarget;public float StopDistance;public bool KeepFollowing;
        internal PerformanceAction(AgentBrain brain,string owner,Action<string> stopped){_brain=brain;_mission=Mission.Current;OwnerToken=owner;}
        /* ACTUAL_TRY_ACQUIRE */
        public bool BeginFollow(Agent a,float distance,bool keepFollowing=false){FollowTarget=a;StopDistance=distance;KeepFollowing=keepFollowing;Follows++;IsWalking=true;WalkResult=null;return true;}
        public bool BeginWalk(Vec3 v,float distance,float timeout){FollowTarget=null;WalkTarget=v;StopDistance=distance;IsWalking=true;WalkResult=null;return true;}
        public bool FaceTarget(Agent a)=>a!=null;public bool PlayAction(string action){Actions++;return true;}
        public void SetPaused(bool pause){Paused=pause;if(pause)IsWalking=false;}
        public void Finish(string reason){_ended=true;IsWalking=false;}
        public void InvalidateManaged(string reason)=>Finish(reason);
        /* ACTUAL_RELEASE_LEASE */
        /* ACTUAL_PAUSE_LEASE */
    }
    public enum SpeechPriority { Dialogue }
    public struct SpeechContext { public static SpeechContext FromBrain(AgentBrain b,Agent speaker=null,Agent listener=null)=>new SpeechContext(); }
    public static class SpeechChannel
    {
        public static List<Action> Queue=new List<Action>();
        public static List<string> Shown=new List<string>();public static List<double> Times=new List<double>();
        /* ACTUAL_SPEECH_DURATION */
        public static void Say(Agent a,string text,SpeechPriority p,SpeechContext c,float duration=0,Action onShown=null,Func<bool> canPublish=null,Func<bool> canForwardToNearby=null)
        { Queue.Add(()=>{if(canPublish==null||canPublish()){Shown.Add(text);Times.Add(CoopMissionBehavior.Current.SimTime);onShown?.Invoke();}}); }
        public static void Flush(){var q=Queue.ToArray();Queue.Clear();foreach(var a in q)a();}
    }
    public class StoryPrepRunner { public bool TryResolvePoint(string id,out Vec3 p){p=new Vec3(20,0,0);return id=="coop_lure_spot"||id=="son_spot";} }
    public class StoryProbeMissionBehavior
    {
        public static StoryProbeMissionBehavior Current;public Dictionary<string,Agent> Actors=new Dictionary<string,Agent>();
        public StoryPrepRunner Prep=new StoryPrepRunner();public bool PlayerHasCrossed;
        public Agent GetAgent(string id)=>id!=null&&Actors.TryGetValue(id,out var a)?a:null;
        public Hero GetHero(string id)=>CoopRecruitment.UseTemporary && id=="son"?new Hero{StringId=id}:null;public bool HasGateOpportunity(Agent a,Agent b)=>NpcSightSystem.Visible;
    }
    public class Hero { public string StringId; }
    public class Npc { public string Id; }
    public static class StoryProbeDefs { public static Npc[] Npcs=new[]{new Npc{Id="son"},new Npc{Id="blocker"},new Npc{Id="gate_sentry"}}; }
    public static class DebugLogger { public static void Log(string s){} }
    public static class SingleRollResolver
    {public static float SimpleChance(int a,int b,float offer)=>.6f;public static bool Roll(float chance,float roll)=>roll>=1-chance;}
    public static class LWNTextHelper
    {
        public static Dictionary<string,string> Texts=new Dictionary<string,string>();
        public static string ResolveText(string key,string fallback)=>Texts.TryGetValue(key,out var text)?text:fallback;public static string GetReplyLanguageInstruction()=>"Chinese";
        public static string ResolveCompound(string key,params(string var,string value)[] pairs)
        {var text=ResolveText(key,key);foreach(var p in pairs)text=text.Replace("{"+p.var+"}",p.value);return text;}
        public static string ResolvePrompt(string key)=>key;public static string GetLanguageRule()=>"English";
    }
    public class Settings {public static Settings Instance=new Settings();public bool IsLLMConfigured,InteractionDisabled;public bool PlotEnabled=true;public bool IsInteractionDisabled()=>InteractionDisabled;}
    public static class ConversationEntryPatch {public static bool Active;public static bool IsConversationActive()=>Active;}
    public static class ModInput {public static bool UsingGamepad;public static bool IsSystemModalActive()=>false;}
    public static class ScreenManager {public static object TopScreen=new object();}
    public static class UiFullScreenHelper
    {public static bool FullScreen;public static bool IsEscapeMenuOpen()=>false;public static bool IsFullScreenUiOpen()=>FullScreen;public static bool IsGameMenuOpen()=>false;public static bool IsCampaignMapScreen()=>true;}
    public static class CooperationGateTests
    {
        /* ACTUAL_COOPERATION_UI_GATE */
    }
    public class LLMService
    {
        public static LLMService Instance=new LLMService();public System.Threading.Tasks.Task<string> ChatOnceAsync(string prompt,int maxTokens,int timeoutMs,bool needJson)=>System.Threading.Tasks.Task.FromResult<string>(null);
        public static string CleanJson(string json)=>json;
    }
    public enum ImMessageKind { Cooperation }
    public class ImMessage {public ImMessageKind Kind;public string CooperationActivityId,SenderHeroId,SenderName,Content,ConvId;public long TimeStamp;}
    public class ImButtonVM {public string Label;public Action Action;public ImButtonVM(string text,Action action,bool enabled=true){Label=text;Action=action;}}
    public static class ImChatManager {public static long NowUnixMs()=>0;}
    public static class NearbyFeed {public static List<ImMessage> Messages=new List<ImMessage>();public static void AppendCard(ImMessage m){m.ConvId="nearby";Messages.Add(m);}}
    public static class ImChatView
    {public static bool AllowShow;public static int Shows;public static bool ShowCooperationCard(){Shows++;return AllowShow;}public static void RebuildCardMessages(){}public static void Close(){} }
}
namespace LivingWorldNpcs.Planner.Cooperation
{
    public enum CoopContractMode { Temporary,Companion }
    public static class CoopRecruitment
    {
        public static bool UseTemporary;public static int FavorDebits;
        public static bool CanCooperate(string id,CoopActivity a,out string reason){reason=null;return true;}
        public static bool CanRequestPurpose(string id,CoopActivity a,string purpose,out string reason){reason=null;return true;}
        public static CoopContractMode GetMode(Hero h)=>UseTemporary?CoopContractMode.Temporary:CoopContractMode.Companion;
        public static bool ConsumeFavor(Hero h,string purpose,double time,string activityId = null)
        { if(string.IsNullOrEmpty(activityId))throw new Exception("missing real activity identity");FavorDebits++;return true; }
    }
    public static class CoopTestSession
    {public static Queue<float> Rolls=new Queue<float>();public static float NextRoll(string id)=>Rolls.Count>0?Rolls.Dequeue():1;public static bool Owns(string id)=>true;}
    public class CoopMissionBehavior
    {
        public static CoopMissionBehavior Current;public CoopMissionRuntime Runtime;public int Reports;public double SimTime=>Runtime.SimTime;
        public CoopMissionBehavior(CoopMissionRuntime r){Runtime=r;}
        public static CoopGoalDef SelectRoleGoal(CoopRoleDef role,CoopContentData content,IDictionary<string,CoopTruth> facts)=>content.Goals.Where(g=>role.GoalIds.Contains(g.Id)&&g.Constraints.IsSatisfiedBy(facts)).OrderByDescending(g=>g.Priority).FirstOrDefault();
        public CoopActivity StartRoleActivity(string actor,CoopRoleDef role,CoopGoalDef goal,IDictionary<string,CoopTruth> facts,string partner,string source,out string reason,
            CoopContentData pinnedContent=null,CoopDomain pinnedDomain=null)
        {
            if(source=="failure_report")Reports++;
            var a=CoopDirector.Current.StartSeeded(actor,role.Id,goal,CoopAlgorithm.Htn,source,0,facts,out reason,pinnedContent,pinnedDomain);
            if(a!=null)a.InteractionPartnerId=partner;return a;
        }
    }
}
