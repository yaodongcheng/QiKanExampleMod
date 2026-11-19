using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using LivingWorldNpcs.Planner.Cooperation;

namespace TaleWorlds.CampaignSystem
{
    public class Hero
    {
        public static Hero MainHero = new Hero { StringId = "player" };
        public static List<Hero> AllAliveHeroes = new List<Hero>();
        public string StringId;
        public object Name = "NPC";
        public bool IsFemale;
        public int Gold;
        public Clan Clan;
        public int GetTraitLevel(object trait) => 0;
    }
    public class Clan { public object Name = "Clan"; public Kingdom Kingdom; }
    public class Kingdom { public object Name = "Kingdom"; }
    public class Campaign { public static Campaign Current = new Campaign(); }
    public class CampaignTime { public static CampaignTime Now = new CampaignTime(); public double ToDays = 7; }
}
namespace TaleWorlds.Core { }
namespace TaleWorlds.Library
{
    public class InformationMessage { public InformationMessage(string text) { } }
    public static class InformationManager { public static int Notices; public static void DisplayMessage(InformationMessage m) { Notices++; } }
}
namespace LivingWorldNpcs
{
    public class CharacterObject { public string StringId; public bool IsHero; public Hero HeroObject; }
    public class Agent { public CharacterObject Character; public int Index; public string Name = "Template"; }
    public class NPCProfile
    {
        public string StringId, Name = "Xiao Liu";
        public int DebtOfGraditude;
        public NPCProfile(Hero hero, Agent agent = null) { StringId = hero?.StringId; }
    }
    public static class DefaultTraits { public static object Honor = new object(); }
    public enum ImHeatTier { Hot, Normal, Cold }
    public static class ImHeatTracker { public static ImHeatTier TierOf(string id) => ImHeatTier.Cold; }
    public static class DebugLogger { public static void Log(string line) { } }
    public static class LWNTextHelper
    {
        public static string ResolveText(string key, string fallback) => fallback;
        public static string ResolvePrompt(string key) => key;
        public static string ResolveCompound(string key, params (string,string)[] vars) => ResolveCompound(key,key,vars);
        public static string ResolveCompound(string key, string fallback, params (string,string)[] vars)
        { foreach(var v in vars) fallback=fallback.Replace("{"+v.Item1+"}",v.Item2); return fallback; }
    }
    public class ChatMessage { public double TimeStamp; public long SeqId; public static void EnsureSeqCounterAbove(long id) { } }
    public class RecentMemory
    {
        public string Content; public double TimeStamp_Start, TimeStamp_End; public float CampaignDay; public long SeqId;
        public RecentMemory(string c,double start,double end,float day) {Content=c; TimeStamp_Start=start; TimeStamp_End=end; CampaignDay=day;}
        public static void EnsureSeqCounterAbove(long id) { }
    }
    public class SingNpcMemorySystem
    {
        public readonly NPCProfile _profile;
        private readonly object _lock = new object();
        public List<ChatMessage> RecentHistory = new List<ChatMessage>();
        public LinkedList<RecentMemory> DynamicMemories = new LinkedList<RecentMemory>();
        public StringBuilder PermanentMemory = new StringBuilder();
        public string BackgroundStory, Personality, Specialty;
        public List<string> ImportantEvents = new List<string>();
        private const int MaxImportantEvents = 12;
        public int MaxDynamicMemoryCount => 2;
        public int MaxPermanentLength => 100;
        public SingNpcMemorySystem(NPCProfile profile) { _profile=profile; }
        public string GetPersonaPrompt() => "persona";
        /* FAVOR_METHODS */
        /* RESTORE_MEMORY */
    }
    /* SAVE_ENTRY */
    /* MEMORY_MANAGER */
    /* SAVE_GUARD */
    public static class AgentControlHelper { /* RESOURCE_ENTRY */ }
    public static class NPCInfoVM
    {
        public static string FormatGameTime(float day) => "Day "+day;
        /* UI_FORMAT */
    }
    public class StoryProbeMissionBehavior
    {
        public static StoryProbeMissionBehavior Current = new StoryProbeMissionBehavior();
        public Hero Hero;
        public Hero GetHero(string actor) => Hero;
    }
    public static class StoryProbeState
    {
        public static bool Turned, Settled;
        public static bool IsQuestTurnedIn(string actor) => Turned;
        public static bool IsStreetEventResolved(int index) => Settled;
    }
    public static class StoryGrant
    {
        private static bool TryInt(string text,out int number) => int.TryParse(text,out number);
        public static bool Commit(Dictionary<string,string> args,out string error)
        {
            error=null;
            var host=StoryProbeMissionBehavior.Current;
            string P(string key) => args.TryGetValue(key,out var value)?value:null;
            switch("favor_grant") { /* GRANT_BRANCH */ }
            return false;
        }
    }
}
namespace LivingWorldNpcs.Planner.Cooperation
{
    public enum CoopContractMode { Temporary }
    public class CoopRecruitmentConfig { public int FavorCost=5; public string FavorSourceKey="LWN_use", FavorSourceFallback="Drew the guards away"; }
    public class Ledger { public void SetRelation(string actor,string key,string value) { } }
    public class CoopDirector { public static CoopDirector Current=new CoopDirector(); public string RunId="run"; public Ledger Ledger=new Ledger(); }
    public static class CoopTestSession { public static SingNpcMemorySystem FavorMemory(Hero hero)=>null; public static bool Owns(string id)=>false; }
    public class CoopLog
    {
        public static CoopLog Event(string text)=>new CoopLog();
        public CoopLog Actor(string text)=>this; public CoopLog ContractMode(string text)=>this;
        public CoopLog Operation(string text)=>this; public CoopLog Raw(string key,object value)=>this;
        public CoopLog SimTime(double time)=>this; public void Write() { }
    }
    public static class CoopRecruitment
    {
        private const string ModeKey="contract.mode";
        public static CoopRecruitmentConfig Config=new CoopRecruitmentConfig();
        /* HAS_FAVOR */
        /* SPEND_FAVOR */
    }
}
public static class Program
{
    private static int _checks;
    private static void Check(bool ok,string label) { _checks++; if(!ok) throw new Exception("FAIL: "+label); }
    public static void Main()
    {
        var hero=new Hero {StringId="son"}; Hero.AllAliveHeroes.Add(hero);
        LivingWorldNpcs.StoryProbeMissionBehavior.Current.Hero=hero;
        var mem=LivingWorldNpcs.AllNpcMemoryManager.GetMemory(hero.StringId);
        string player=Hero.MainHero.StringId;
        Check(mem.FavorBalance(player)==0,"old profile default gives no free favor");
        var grant=new Dictionary<string,string> { {"npc","son"},{"amount","2"},{"source","tools"},{"key","LWN_tools"},{"fallback","Recovered tools"},{"quest","son"} };
        Check(!LivingWorldNpcs.StoryGrant.Commit(grant,out _),"no reward before real turnin");
        Check(mem.FavorBalance(player)==0,"failed turnin changes nothing");
        LivingWorldNpcs.StoryProbeState.Turned=true;
        Check(LivingWorldNpcs.StoryGrant.Commit(grant,out _),"completed tools reward");
        Check(mem.FavorBalance(player)==2,"tools give exactly two");
        Check(!CoopRecruitment.HasFavor(hero),"two cannot afford five");
        Check(!CoopRecruitment.ConsumeFavor(hero,"talk",1,"early"),"insufficient cannot spend");
        Check(LivingWorldNpcs.StoryGrant.Commit(grant,out _),"repeat reward acknowledged");
        Check(mem.FavorBalance(player)==2&&mem.SnapshotFavorRecords(player).Count==1,"duplicate business source");
        grant.Remove("quest");grant["event"]="4";grant["amount"]="3";grant["source"]="dispute";grant["key"]="LWN_dispute";grant["fallback"]="Settled dispute";
        Check(!LivingWorldNpcs.StoryGrant.Commit(grant,out _),"unresolved dispute cannot reward");
        LivingWorldNpcs.StoryProbeState.Settled=true;
        Check(LivingWorldNpcs.StoryGrant.Commit(grant,out _),"success dispute reward");
        Check(mem.FavorBalance(player)==5&&CoopRecruitment.HasFavor(hero),"two and three unlock request");
        Check(LivingWorldNpcs.NPCInfoVM.BuildFavorText(mem).Contains("Recovered tools")
            &&LivingWorldNpcs.NPCInfoVM.BuildFavorText(mem).Contains("Settled dispute"),"UI exposes both real sources");
        var copy=mem.SnapshotFavorRecords();copy[0].Amount=999;
        Check(mem.FavorBalance(player)==5,"snapshots do not mutate authority");
        var snapshots=Enumerable.Range(0,24).Select(LivingWorldNpcs.AllNpcMemoryManager.SerializeSlot).ToArray();
        var pages=Enumerable.Range(0,24).Select(LivingWorldNpcs.AllNpcMemoryManager.SerializeFavorPages).ToArray();
        LivingWorldNpcs.AllNpcMemoryManager.ResetActiveMemories();
        for(int slot=0;slot<24;slot++) {LivingWorldNpcs.AllNpcMemoryManager.DeserializeSlot(slot,snapshots[slot]);LivingWorldNpcs.AllNpcMemoryManager.DeserializeFavorPages(slot,pages[slot]);}
        // Hero not interacted with: a second save must still carry pending accounts.
        Check(Enumerable.Range(0,24).SelectMany(LivingWorldNpcs.AllNpcMemoryManager.SerializeFavorPages).Count()>0,"pending account resaves");
        mem=LivingWorldNpcs.AllNpcMemoryManager.GetMemory("son");
        Check(mem.FavorBalance(player)==5&&mem._profile.DebtOfGraditude==5,"load restores ledger and profile projection");
        Check(CoopRecruitment.ConsumeFavor(hero,"talk",2,"request"),"real request consumes five");
        Check(mem.FavorBalance(player)==0&&!CoopRecruitment.HasFavor(hero),"zero balance gates further requests");
        Check(!CoopRecruitment.ConsumeFavor(hero,"talk",3,"second_guard"),"second guard cannot debit again");
        Check(mem.SnapshotFavorRecords(player).Count==3&&LivingWorldNpcs.NPCInfoVM.BuildFavorText(mem).Contains("Drew the guards away"),"spend source kept at zero");
        Check(LivingWorldNpcs.StoryGrant.Commit(grant,out _)&&mem.FavorBalance(player)==0,"post spend replay cannot farm rewards");
        var zero=Enumerable.Range(0,24).Select(LivingWorldNpcs.AllNpcMemoryManager.SerializeFavorPages).ToArray();
        LivingWorldNpcs.AllNpcMemoryManager.ResetActiveMemories();
        for(int slot=0;slot<24;slot++) LivingWorldNpcs.AllNpcMemoryManager.DeserializeFavorPages(slot,zero[slot]);
        mem=LivingWorldNpcs.AllNpcMemoryManager.GetMemory("son");
        Check(mem.FavorBalance(player)==0&&mem.SnapshotFavorRecords(player).Count==3,"zero balance survives without ordinary memory");
        Check(LivingWorldNpcs.StoryGrant.Commit(grant,out _)&&mem.FavorBalance(player)==0,"read reload retains business receipts");
        bool changed;
        Check(!mem.TryChangeFavor(player,"overdraft",-1,"LWN_use","Use",0,0,out changed),"insufficient balance");
        Check(mem.TryChangeFavor("other","other.earned",9,"LWN_other","Other player",0,0,out changed),"separate player account");
        Check(mem.FavorBalance(player)==0&&mem.FavorBalance("other")==9,"player accounts do not leak");
        Check(mem._profile.DebtOfGraditude==0,"profile remains current player projection");
        var reverse = new LivingWorldNpcs.SingNpcMemorySystem(new LivingWorldNpcs.NPCProfile(hero));
        reverse.TryChangeFavor(player,"event.first",3,"LWN_dispute","Dispute",0,0,out changed);
        reverse.TryChangeFavor(player,"tools.second",2,"LWN_tools","Tools",0,0,out changed);
        Check(reverse.FavorBalance(player)==5,"either real reward order reaches five");
        for(int i=0;i<300;i++) mem.TryChangeFavor(player,"source."+i,1,"LWN_source",new string('\u4e2d',80),i,7,out changed);
        var large=Enumerable.Range(0,24).Select(LivingWorldNpcs.AllNpcMemoryManager.SerializeFavorPages).ToArray();
        Check(large.Sum(p=>p.Count)>1,"large account uses multiple safe pages");
        Check(large.SelectMany(p=>p).All(p=>Encoding.UTF8.GetByteCount(p)<=28000),"UTF8 byte cap");
        LivingWorldNpcs.AllNpcMemoryManager.ResetActiveMemories();
        for(int slot=0;slot<24;slot++) LivingWorldNpcs.AllNpcMemoryManager.DeserializeFavorPages(slot,large[slot]);
        mem=LivingWorldNpcs.AllNpcMemoryManager.GetMemory("son");
        Check(mem.FavorBalance(player)==300&&mem.SnapshotFavorRecords(player).Count==303,"all sources survive trimmed ordinary memory");
        var before=mem.SnapshotFavorRecords();mem.TryChangeFavor(player,"temporary",7,"LWN_temp","Temp",0,0,out changed);mem.RestoreFavorRecords(before);
        Check(mem.FavorBalance(player)==300,"effect snapshot rollback");
        LivingWorldNpcs.AllNpcMemoryManager.ResetActiveMemories();
        Check(LivingWorldNpcs.AllNpcMemoryManager.GetMemory("son").FavorBalance(player)==0,"cross save reset clears accounts");
        var template=LivingWorldNpcs.AllNpcMemoryManager.GetMemoryForAgent(new LivingWorldNpcs.Agent { Character=new LivingWorldNpcs.CharacterObject(),Index=1 });
        Check(template.TryChangeFavor(player,"template",1,"LWN_test","Template",0,0,out changed),"templates use same memory ledger");
        Check(!Enumerable.Range(0,24).SelectMany(LivingWorldNpcs.AllNpcMemoryManager.SerializeFavorPages).Any(),"unstable template identity is not persisted");
        Console.WriteLine("PASS: "+_checks+" production NPC favor checks");
    }
}
