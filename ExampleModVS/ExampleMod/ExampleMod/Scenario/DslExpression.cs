using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LivingWorldNpcs
{
    // ============================================================
    // DSL 条件表达式：值系统 + 解析器 + 节点求值（01 语法权威）
    // 语法（产物实测形态）：and(...)/or(...)/not(...) 组合、(引用) op (引用|字面量)、
    //   函数( args )、裸引用真值。比较 op ∈ { == != > >= < <= }。
    // 🔴 安全兜底（01/铁律 1）：引用求值失败 → Null → 任何比较 = false；未知域/属性/函数 =
    //   Null + 日志（UndecidableLog 供 custom.dsl_validate 汇总）。
    // ============================================================

    public enum DslValueKind { Number, Bool, String, Null }

    public class DslValue
    {
        public DslValueKind Kind = DslValueKind.Null;
        public long Num;
        public bool Bool;
        public string Str;

        public static readonly DslValue Null = new DslValue { Kind = DslValueKind.Null };
        public static DslValue FromNumber(long n) => new DslValue { Kind = DslValueKind.Number, Num = n };
        public static DslValue FromBool(bool b) => new DslValue { Kind = DslValueKind.Bool, Bool = b };
        public static DslValue FromString(string s) => new DslValue { Kind = DslValueKind.String, Str = s ?? "" };

        /// <summary>裸引用真值判定（01：裸引用作叶子 = 真值判断）</summary>
        public bool AsBool() => Kind == DslValueKind.Bool ? Bool : Kind == DslValueKind.Number ? Num != 0 : false;

        public bool IsNull => Kind == DslValueKind.Null;
    }

    /// <summary>引用解析回调：AttributeResolver 实现（域读链：引擎/仓/数据包 + 空值兜底）</summary>
    public interface IScenarioQuery
    {
        /// <summary>attr 为 null = 对象存在性/引用值；否则 = 属性求值</summary>
        DslValue Resolve(string domain, string id, string attr);
    }

    public abstract class DslNode
    {
        public abstract DslValue Eval(ScenarioContext ctx);
        public virtual bool IsRef => false;
        public virtual string RefString => null;   // 引用拼「域::id」文本（函数参数用）
    }

    public class DslBoolOp : DslNode
    {
        public enum Op { And, Or, Not }
        public Op Kind;
        public List<DslNode> Items;

        public override DslValue Eval(ScenarioContext ctx)
        {
            switch (Kind)
            {
                case Op.And:
                    bool any = Items.Any(n => !n.Eval(ctx).AsBool());
                    return DslValue.FromBool(!any);
                case Op.Or:
                    return DslValue.FromBool(Items.Any(n => n.Eval(ctx).AsBool()));
                default:
                    return DslValue.FromBool(!Items[0].Eval(ctx).AsBool());
            }
        }
    }

    public class DslCompare : DslNode
    {
        public string Op;  // == != > >= < <=
        public DslNode Left, Right;

        public override DslValue Eval(ScenarioContext ctx)
        {
            var l = Left.Eval(ctx);
            var r = Right.Eval(ctx);
            if (l.IsNull || r.IsNull) return DslValue.FromBool(false);   // 任何引用失败 → 假（01 兜底）

            // 带序枚举（身份/官职链）：字符串参与 > < 比较 → 查 RankLadder（17 数据包）
            if (Op != "==" && Op != "!=" && l.Kind == DslValueKind.String && r.Kind == DslValueKind.String)
            {
                var lr = ScenarioDataPack.GetIdentityRank(l.Str);
                var rr = ScenarioDataPack.GetIdentityRank(r.Str);
                if (lr == null || rr == null)
                {
                    AttributeResolver.ReportUndecidable($"带序比较缺等级表（RankLadder）: {l.Str} {Op} {r.Str}");
                    return DslValue.FromBool(false);
                }
                l = DslValue.FromNumber(lr.Value);
                r = DslValue.FromNumber(rr.Value);
            }
            else if (l.Kind != r.Kind || (l.Kind == DslValueKind.Bool && Op != "==" && Op != "!="))
            {
                AttributeResolver.ReportUndecidable($"类型不匹配比较: {l.Kind} {Op} {r.Kind}");
                return DslValue.FromBool(false);
            }

            switch (Op)
            {
                case "==": return DslValue.FromBool(EqualsValue(l, r));
                case "!=": return DslValue.FromBool(!EqualsValue(l, r));
                case ">": return DslValue.FromBool(l.Num > r.Num);
                case ">=": return DslValue.FromBool(l.Num >= r.Num);
                case "<": return DslValue.FromBool(l.Num < r.Num);
                case "<=": return DslValue.FromBool(l.Num <= r.Num);
            }
            return DslValue.FromBool(false);
        }

        private static bool EqualsValue(DslValue a, DslValue b)
        {
            if (a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case DslValueKind.Number: return a.Num == b.Num;
                case DslValueKind.Bool: return a.Bool == b.Bool;
                case DslValueKind.String: return string.Equals(a.Str, b.Str, StringComparison.Ordinal);
            }
            return false;
        }
    }

    public class DslFuncCall : DslNode
    {
        public string Name;
        public List<DslNode> Args;

        public override DslValue Eval(ScenarioContext ctx) => AttributeResolver.CallFunction(Name, Args, ctx);
    }

    public class DslRef : DslNode
    {
        public string Domain;    // Hero:: / Clan:: / Settlement:: / Faction:: / Region:: / Time:: / Flag:: / Ctx:: / Event:: / Variable:: / GlobalSlot::
        public string Id;        // StringId（Faction::Kingdom.oda 含点）
        public string Attr;      // 属性名（域正则拆法放 resolver）

        public override DslValue Eval(ScenarioContext ctx)
        {
            if (Domain == "Ctx::")
            {
                // Ctx 槽值 = 对象引用（"Hero::xx"）——attr 存在 = 以槽值为对象继续解析（16 §一：Ctx 引用可带域属性）
                string slotVal = ctx != null
                    ? (Id == "event_settlement" ? ctx.EventSettlement : Id == "event_hero" ? ctx.EventHero : ctx.Get(Id))
                    : null;
                if (string.IsNullOrEmpty(slotVal)) return DslValue.Null;
                if (Attr == null) return DslValue.FromString(slotVal);
                int sep = slotVal.IndexOf("::");
                if (sep < 0) return DslValue.Null;
                return AttributeResolver.Query.Resolve(slotVal.Substring(0, sep + 2), slotVal.Substring(sep + 2), Attr);
            }
            return AttributeResolver.Query.Resolve(Domain, Id, Attr);
        }

        public override bool IsRef => true;
        public override string RefString => Domain + Id;
    }

    public class DslLiteral : DslNode
    {
        public DslValue Value;
        public override DslValue Eval(ScenarioContext ctx) => Value;
    }

    // ------------------------------------------------------------
    // 解析器（递归下降）
    // ------------------------------------------------------------
    public static class DslParser
    {
        private static int _pos;
        private static string _s;
        private static bool _err;

        public static DslNode Parse(string expression)
        {
            _s = expression ?? "";
            _pos = 0;
            _err = false;
            var node = ParseExpr();
            if (_err) return null;
            SkipWs();
            return _pos >= _s.Length ? node : null;
        }

        private static DslNode ParseExpr()
        {
            string w = PeekWord();
            if (w == "or") { _pos += w.Length; return ParseArgs("or", DslBoolOp.Op.Or); }
            if (w == "and") { _pos += w.Length; return ParseArgs("and", DslBoolOp.Op.And); }
            return ParseCompare();
        }

        private static DslNode ParseArgs(string name, DslBoolOp.Op op)
        {
            if (!Expect('(')) { _err = true; return null; }
            var items = new List<DslNode>();
            if (!Peek(')'))
                while (true)
                {
                    items.Add(ParseExpr());
                    if (!Expect(',')) break;
                }
            if (!Expect(')')) { _err = true; return null; }
            if (name == "not") return new DslBoolOp { Kind = DslBoolOp.Op.Not, Items = items };
            return new DslBoolOp { Kind = op, Items = items };
        }

        private static DslNode ParseCompare()
        {
            var left = ParseAtom();
            SkipWs();
            string op = PeekOp();
            if (op != null)
            {
                _pos += op.Length;
                var right = ParseAtom();
                return new DslCompare { Op = op, Left = left, Right = right };
            }
            return left;
        }

        private static DslNode ParseAtom()
        {
            SkipWs();
            if (Peek('('))
            {
                _pos++;
                var inner = ParseExpr();
                if (!Expect(')')) { _err = true; return null; }
                return inner;
            }
            string w = PeekWord();
            if (w == null) { _err = true; return null; }

            if (w == "not") { _pos += w.Length; return ParseArgs("not", DslBoolOp.Op.Not); }

            // 字面量关键词
            if (w == "true") { _pos += 4; return new DslLiteral { Value = DslValue.FromBool(true) }; }
            if (w == "false") { _pos += 5; return new DslLiteral { Value = DslValue.FromBool(false) }; }
            if (w == "null") { _pos += 4; return new DslLiteral { Value = DslValue.Null }; }

            char c = _s[_pos];
            if (c == '"')
            {
                int end = _s.IndexOf('"', _pos + 1);
                if (end < 0) { _err = true; return null; }
                string lit = _s.Substring(_pos + 1, end - _pos - 1);
                _pos = end + 1;
                return new DslLiteral { Value = DslValue.FromString(lit) };
            }
            if (char.IsDigit(c) || (c == '-' && _pos + 1 < _s.Length && char.IsDigit(_s[_pos + 1])))
            {
                int end = _pos;
                while (end < _s.Length && (char.IsDigit(_s[end]) || (_s[end] == '-' && end == _pos))) end++;
                if (long.TryParse(_s.Substring(_pos, end - _pos), out var num))
                {
                    _pos = end;
                    return new DslLiteral { Value = DslValue.FromNumber(num) };
                }
                _err = true;
                return null;
            }

            _pos += w.Length;
            SkipWs();
            if (Peek('('))   // 函数调用
            {
                _pos++;
                var args = new List<DslNode>();
                if (!Peek(')'))
                    while (true)
                    {
                        args.Add(ParseAtom());
                        if (!Expect(',')) break;
                    }
                if (!Expect(')')) { _err = true; return null; }
                return new DslFuncCall { Name = w, Args = args };
            }
            // 引用：domain::id[.attr]——域 = 分割
            int sep = w.IndexOf("::");
            if (sep < 0) { _err = true; return null; }
            string domain = w.Substring(0, sep + 2);
            string rest = w.Substring(sep + 2);
            string attr = null;
            if (domain != "Faction::" && domain != "Event::" && rest.IndexOf('.') >= 0)
            {
                int dot = rest.LastIndexOf('.');
                attr = rest.Substring(dot + 1);
                rest = rest.Substring(0, dot);
            }
            return new DslRef { Domain = domain, Id = rest, Attr = attr };
        }

        private static bool Expect(char c)
        {
            SkipWs();
            if (_pos < _s.Length && _s[_pos] == c) { _pos++; return true; }
            return false;
        }

        private static bool Peek(char c)
        {
            SkipWs();
            return _pos < _s.Length && _s[_pos] == c;
        }

        private static void SkipWs()
        {
            while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++;
        }

        private static string PeekOp()
        {
            if (_pos + 1 < _s.Length)
            {
                string two = _s.Substring(_pos, 2);
                if (two == "==" || two == "!=" || two == ">=" || two == "<=") return two;
            }
            if (_pos < _s.Length && (_s[_pos] == '>' || _s[_pos] == '<')) return _s[_pos].ToString();
            return null;
        }

        private static string PeekWord()
        {
            SkipWs();
            int start = _pos;
            while (_pos < _s.Length && !char.IsWhiteSpace(_s[_pos]) && !")?,".Contains(_s[_pos]) && _s[_pos] != '(' && _s[_pos] != ',' && _s[_pos] != ')' && _s[_pos] != '=' && _s[_pos] != '!' && _s[_pos] != '<' && _s[_pos] != '>')
                _pos++;
            if (_pos == start) { _pos = start; return null; }
            string w = _s.Substring(start, _pos - start);
            _pos = start;
            return w;
        }
    }

    /// <summary>求值入口：Evaluate（bool，失败=false）/ EvaluateDetailed（分档：可判定/不可判定）</summary>
    public static class DslEvaluator
    {
        public static bool Evaluate(string expression, ScenarioContext ctx = null)
        {
            return EvaluateDetailed(expression, ctx).Result;
        }

        public static DslEvaluation EvaluateDetailed(string expression, ScenarioContext ctx = null)
        {
            var ev = new DslEvaluation();
            try
            {
                var node = DslParser.Parse(expression);
                if (node == null)
                {
                    ev.Error = "解析失败";
                    return ev;
                }
                ev.Result = node.Eval(ctx ?? ScenarioContext.Instance).AsBool();
                ev.Undecidable = new List<string>(AttributeResolver.UndecidableLog);
                AttributeResolver.UndecidableLog.Clear();
                return ev;
            }
            catch (Exception e)
            {
                ev.Error = e.Message;
                return ev;
            }
        }
    }

    public class DslEvaluation
    {
        public bool Result;
        public string Error;
        public List<string> Undecidable = new List<string>();
    }
}
