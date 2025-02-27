﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Z3;
using System.Collections.Immutable;
using DataLabeling.Color;

namespace DataLabeling
{
    public enum Form { DNF, CNF }

    public abstract class Ir
    {
        public BoolExpr ToggleVar;

        protected Ir(BoolExpr toggleVar) {
            ToggleVar = toggleVar;
        }

        public abstract Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example);

        public abstract BoolExpr ToZ3(Optimize optimize, Context ctx, Form form);

        public abstract ArithExpr? ToggleVarSum(Context ctx);

        public abstract BooleanAst? Compile(Model z3Solution);

        protected bool ToggleSolution(Model z3Solution) {
            return z3Solution.Eval(ToggleVar).BoolValue == Z3_lbool.Z3_L_TRUE;
        }

        public abstract List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars);

        // CollectToggleVars returns a tuple of two lists. The first list is those toggle variables
        // which are positive (ie, a plain match). The secon list is toggle variables which are negative
        // (ie, destructive aka not matches)
        public List<Tuple<BoolExpr, uint>> CollectToggleVars() {
            return CollectToggleVars(new List<Tuple<BoolExpr, uint>>());
        }
    }

    public class OrIr : Ir
    {
        public List<Ir> Inner { get; private set; }

        public OrIr(List<Ir> inner, BoolExpr toggleVar) : base(toggleVar) {
            Inner = inner;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            return new OrIr(Inner.Select(node => node.Apply(env, example)).ToList(), ToggleVar);
        }

        public override ArithExpr ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                ArithExpr total = (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0));
                foreach (Ir r in Inner) {
                    total = total + r.ToggleVarSum(ctx);
                }
                return total;
            } else {
                ArithExpr? total = null;
                foreach (Ir r in Inner) {
                    ArithExpr? res = r.ToggleVarSum(ctx);
                    if (total == null) {
                        total = res;
                    } else if (res != null) {
                        total = total + r.ToggleVarSum(ctx);
                    }
                }
                return total;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            if (form == Form.DNF) {
                if (ToggleVar == null) {
                    // This Or clause is in the outermost form, so we should ignore any toggle variable behaviour
                    return ctx.MkOr(Inner.Select(x => x.ToZ3(optimize, ctx, form)).ToArray());
                } else {
                    // This Or clause is embedded within an And clause as part of an "Any"
                    return ctx.MkImplies(ToggleVar, ctx.MkOr(Inner.Select(x => x.ToZ3(optimize, ctx, form)).ToArray()));
                }
            } else if (form == Form.CNF) {
                if (ToggleVar == null) {
                    // This Or clause is embedded within an and clause, so we should compute an equivalent toggle variable
                    return ctx.MkOr(ctx.MkNot(ctx.MkOr(Inner.Select(x => x.ToggleVar).ToArray())), ctx.MkOr(Inner.Select(x => x.ToZ3(optimize, ctx, form)).ToArray()));
                } else {
                    // This Or clause is embedded within an Or clause as part of an "Any"
                    return ctx.MkAnd(ToggleVar, ctx.MkOr(Inner.Select(x => x.ToZ3(optimize, ctx, form)).ToArray()));
                }
            }

            throw new NotImplementedException();
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                List<BooleanAst> compiled = Inner.Select(x => x.Compile(z3Solution)).Where(x => x != null).ToList();
                if (compiled.Count > 0) {
                    return compiled.Aggregate((BooleanAst left, BooleanAst right) => new OrBool(left, right));
                } else {
                    return null;
                }
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            foreach (Ir ir in Inner) {
                ir.CollectToggleVars(toggleVars);
            }
            return toggleVars;
        }
    }

    public class AndIr : Ir
    {
        public List<Ir> Inner { get; private set; }

        public AndIr(List<Ir> inner, BoolExpr toggleVar) : base(toggleVar) {
            Inner = inner;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            return new AndIr(Inner.Select(node => node.Apply(env, example)).ToList(), ToggleVar);
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                ArithExpr total = (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0));
                foreach (Ir r in Inner) {
                    total = total + r.ToggleVarSum(ctx);
                }
                return total;
            } else {
                ArithExpr? total = null;
                foreach (Ir r in Inner) {
                    ArithExpr? res = r.ToggleVarSum(ctx);
                    if (total == null) {
                        total = res;
                    } else if (res != null) {
                        total = total + r.ToggleVarSum(ctx);
                    }
                }
                return total;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            if (form == Form.DNF) {
                if (ToggleVar == null) {
                    // This And clause is embedded within an Or clause, so we should compute the equivalent toggle variable
                    return ctx.MkAnd(ctx.MkOr(Inner.Select(x => x.ToggleVar).ToArray()), ctx.MkAnd(Inner.Select(x => x.ToZ3(optimize, ctx, form)).ToArray()));
                } else {
                    // This And clause is embedded within an And clause as part of an "All"
                    return ctx.MkImplies(ToggleVar, ctx.MkAnd(Inner.Select(x => x.ToZ3(optimize, ctx, form)).ToArray()));
                }
            } else if (form == Form.CNF) {
                if (ToggleVar == null) {
                    // This And clause in the outermost form, and we should ignore any toggles
                    return ctx.MkAnd(Inner.Select(x => x.ToZ3(optimize, ctx, form)).ToArray());
                } else {
                    // This And clause is embedded within an Or clause as part of an "All"
                    return ctx.MkAnd(ToggleVar, ctx.MkAnd(Inner.Select(x => x.ToZ3(optimize, ctx, form)).ToArray()));
                }
            }

            throw new NotImplementedException();
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                List<BooleanAst> compiled = Inner.Select(x => x.Compile(z3Solution)).Where(x => x != null).ToList();
                if (compiled.Count > 0) {
                    return compiled.Aggregate((BooleanAst left, BooleanAst right) => new AndBool(left, right));
                } else {
                    return null;
                }
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            foreach (Ir ir in Inner) {
                ir.CollectToggleVars(toggleVars);
            }
            return toggleVars;
        }
    }

    public class AnyIr : Ir
    {
        public readonly ObjectVariable ObjVar;
        public readonly Ir Inner;

        public AnyIr(ObjectVariable objVar, Ir inner, BoolExpr toggleVar) : base(toggleVar) {
            ObjVar = objVar;
            Inner = inner;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            return new OrIr(example.GetBoxes().Select(box => Inner.Apply(env.Add(ObjVar, Tuple.Create(box, example.GetBase(box))), example)).ToList(), ToggleVar);
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0)) + Inner.ToggleVarSum(ctx);
            } else {
                return Inner.ToggleVarSum(ctx);
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            throw new NotImplementedException("Unable to convert partially compiled formula to z3 form. This formula contains an any expression. Try compiling to completely reify all any statements");
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                return new Exists(ObjVar, Inner.Compile(z3Solution));
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            Inner.CollectToggleVars(toggleVars);
            return toggleVars;
        }
    }

    public class AllIr : Ir
    {
        public readonly ObjectVariable ObjVar;
        public readonly Ir Inner;

        public AllIr(ObjectVariable objVar, Ir inner, BoolExpr toggleVar) : base(toggleVar) {
            ObjVar = objVar;
            Inner = inner;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            return new AndIr(example.GetBoxes().Select(box => Inner.Apply(env.Add(ObjVar, Tuple.Create(box, example.GetBase(box))), example)).ToList(), ToggleVar);
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0)) + Inner.ToggleVarSum(ctx);
            } else {
                return Inner.ToggleVarSum(ctx);
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            throw new NotImplementedException("Unable to convert partially compiled formula to z3 form. This formula contains an all expression. Try compiling to completely reify all all statements");
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                return new Forall(ObjVar, Inner.Compile(z3Solution));
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            Inner.CollectToggleVars(toggleVars);
            return toggleVars;
        }
    }

    public class BooleanIr : Ir
    {
        public readonly bool Value;

        public BooleanIr(bool value, BoolExpr toggleVar) : base(toggleVar) {
            Value = value;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            return this;
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr) ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            if (form == Form.DNF) {;
                return ctx.MkImplies(ToggleVar, ctx.MkBool(Value));
            } else {
                return ctx.MkAnd(ToggleVar, ctx.MkBool(Value));
            }
        }

        public override BooleanAst? Compile(Model z3Solution) {
            throw new NotImplementedException("Cannot compile a boolean to an AST representation");
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            return toggleVars;
        }
    }

    public class LabelIsIr : Ir
    {
        public readonly ObjectVariable ObjVar;
        public readonly ObjectLiteral ObjLit;
        public readonly bool Negated;

        public const int COST = 1;
        public const int NEGATED_COST = 2;

        public LabelIsIr(ObjectVariable objVar, ObjectLiteral objLit, bool negated, BoolExpr toggleVar) : base(toggleVar) {
            ObjVar = objVar;
            ObjLit = objLit;
            Negated = negated;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            if (env.ContainsKey(ObjVar)) {
                ObjectLiteral binding = env[ObjVar].Item2;
                bool eq = binding.Equals(ObjLit);
                if (Negated) {
                    return new BooleanIr(!eq, ToggleVar);
                } else {
                    return new BooleanIr(eq, ToggleVar);
                }
            } else {
                throw new ArgumentException("Could not find variable named " + ObjVar.ToString() + " in passed environment");
            }
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(Negated ? NEGATED_COST : COST), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            throw new NotImplementedException("Unable to convert partially compiled formula to z3 form. This formula contains a match expression. Try compiling to completely reify all match statements");
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                LabelIs m = new LabelIs(ObjVar, ObjLit);
                if (Negated) {
                    return new NotBool(m);
                } else {
                    return m;
                }
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, (uint) (Negated ? NEGATED_COST : COST)));
            }
            return toggleVars;
        }
    }

    public class EqualLabelIr : Ir
    {
        public readonly ObjectVariable ObjA;
        public readonly ObjectVariable ObjB;
        public readonly bool Negated;

        public const int COST = 2;
        public const int NEGATED_COST = 3;

        public EqualLabelIr(ObjectVariable objA, ObjectVariable objB, bool negated, BoolExpr toggleVar) : base(toggleVar) {
            ObjA = objA;
            ObjB = objB;
            Negated = negated;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            if (env.ContainsKey(ObjA) && env.ContainsKey(ObjB)) {
                ObjectLiteral boundA = env[ObjA].Item2;
                ObjectLiteral boundB = env[ObjB].Item2;
                bool eq = boundA.Equals(boundB);
                if (Negated) {
                    return new BooleanIr(!eq, ToggleVar);
                } else {
                    return new BooleanIr(eq, ToggleVar);
                }
            } else {
                throw new ArgumentException("Unable to find " + ObjA.ToString() + " or " + ObjB.ToString() + " in the current environment");
            }
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(Negated ? NEGATED_COST : COST), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            throw new NotImplementedException("Unable to convert partially compiled formula to z3 form. This formula contains a match expression. Try compiling to completely reify all match statements");
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                EqualLabel m = new EqualLabel(ObjA, ObjB);
                if (Negated) {
                    return new NotBool(m);
                } else {
                    return m;
                }
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, (uint)(Negated ? NEGATED_COST : COST)));
            }
            return toggleVars;
        }
    }

    public class GeqIr : Ir
    {
        public readonly double Val;
        public readonly RealExpr ComparisonVar;

        public GeqIr(double val, RealExpr comparisonVar, BoolExpr toggleVar) : base(toggleVar) {
            Val = val;
            ComparisonVar = comparisonVar;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            return this;
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            if (form == Form.DNF) {
                return ctx.MkImplies(ToggleVar, ctx.MkReal(Val.ToString()) >= ComparisonVar);
            } else if (form == Form.CNF) {
                return ctx.MkAnd(ToggleVar, ctx.MkReal(Val.ToString()) >= ComparisonVar);
            }

            throw new NotImplementedException();
        }

        public override BooleanAst? Compile(Model z3Solution) {
            throw new NotImplementedException("Cannot compile a boolean to an AST representation");
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            return toggleVars;
        }
    }

    public class IOUIr : Ir
    {
        public readonly ObjectVariable ObjA;
        public readonly ObjectVariable ObjB;
        public RealExpr ComparisonVar;

        public IOUIr(ObjectVariable objA, ObjectVariable objB, RealExpr comparisonVar, BoolExpr toggleVar) : base(toggleVar) {
            ObjA = objA;
            ObjB = objB;
            ComparisonVar = comparisonVar;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            if (env.ContainsKey(ObjA) && env.ContainsKey(ObjB)) {
                BoundingBox boxA = env[ObjA].Item1;
                BoundingBox boxB = env[ObjB].Item1;
                return new GeqIr(boxA.JaccardIndex(boxB), ComparisonVar, ToggleVar);
            } else {
                throw new KeyNotFoundException("Unable to find keys in environment when applying an IOU node");
            }
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr) ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            throw new NotImplementedException("Unable to convert partially compiled formula to z3 form. This formula contains a IOU expression. Try compiling to completely reify all IOU statements");
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                return new IOU(ObjA, ObjB, ((RatNum)z3Solution.Eval(ComparisonVar)).Double);
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            return toggleVars;
        }
    }

    public class LeftIr : Ir
    {
        public readonly ObjectVariable ObjA;
        public readonly ObjectVariable ObjB;

        public LeftIr(ObjectVariable objA, ObjectVariable objB, BoolExpr toggleVar) : base(toggleVar) {
            ObjA = objA;
            ObjB = objB;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            if (env.ContainsKey(ObjA) && env.ContainsKey(ObjB)) {
                BoundingBox boxA = env[ObjA].Item1;
                BoundingBox boxB = env[ObjB].Item1;
                double aX = boxA.Left + (boxA.Width / 2.0);
                double bX = boxB.Left + (boxB.Width / 2.0);
                return new BooleanIr(aX <= bX, ToggleVar);
            } else {
                throw new KeyNotFoundException("Unable to find keys in environment when applying a Left node");
            }
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            throw new NotImplementedException("Unable to convert partially compiled formula to z3 form. This formula contains a IOU expression. Try compiling to completely reify all IOU statements");
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                return new Left(ObjA, ObjB);
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            return toggleVars;
        }
    }

    public class RightIr : Ir
    {
        public readonly ObjectVariable ObjA;
        public readonly ObjectVariable ObjB;

        public RightIr(ObjectVariable objA, ObjectVariable objB, BoolExpr toggleVar) : base(toggleVar) {
            ObjA = objA;
            ObjB = objB;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            if (env.ContainsKey(ObjA) && env.ContainsKey(ObjB)) {
                BoundingBox boxA = env[ObjA].Item1;
                BoundingBox boxB = env[ObjB].Item1;
                double aX = boxA.Left + (boxA.Width / 2.0);
                double bX = boxB.Left + (boxB.Width / 2.0);
                return new BooleanIr(aX >= bX, ToggleVar);
            } else {
                throw new KeyNotFoundException("Unable to find keys in environment when applying a Left node");
            }
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            throw new NotImplementedException("Unable to convert partially compiled formula to z3 form. This formula contains a IOU expression. Try compiling to completely reify all IOU statements");
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                return new Right(ObjA, ObjB);
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            return toggleVars;
        }
    }

    public class AboveIr : Ir
    {
        public readonly ObjectVariable ObjA;
        public readonly ObjectVariable ObjB;

        public AboveIr(ObjectVariable objA, ObjectVariable objB, BoolExpr toggleVar) : base(toggleVar) {
            ObjA = objA;
            ObjB = objB;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            if (env.ContainsKey(ObjA) && env.ContainsKey(ObjB)) {
                BoundingBox boxA = env[ObjA].Item1;
                BoundingBox boxB = env[ObjB].Item1;
                double aY = boxA.Top + (boxA.Height / 2.0);
                double bY = boxB.Top + (boxB.Height / 2.0);
                return new BooleanIr(aY <= bY, ToggleVar);
            } else {
                throw new KeyNotFoundException("Unable to find keys in environment when applying a Left node");
            }
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            throw new NotImplementedException("Unable to convert partially compiled formula to z3 form. This formula contains a IOU expression. Try compiling to completely reify all IOU statements");
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                return new Above(ObjA, ObjB);
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            return toggleVars;
        }
    }

    public class BelowIr : Ir
    {
        public readonly ObjectVariable ObjA;
        public readonly ObjectVariable ObjB;

        public BelowIr(ObjectVariable objA, ObjectVariable objB, BoolExpr toggleVar) : base(toggleVar) {
            ObjA = objA;
            ObjB = objB;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            if (env.ContainsKey(ObjA) && env.ContainsKey(ObjB)) {
                BoundingBox boxA = env[ObjA].Item1;
                BoundingBox boxB = env[ObjB].Item1;
                double aY = boxA.Top + (boxA.Height / 2.0);
                double bY = boxB.Top + (boxB.Height / 2.0);
                return new BooleanIr(aY >= bY, ToggleVar);
            } else {
                throw new KeyNotFoundException("Unable to find keys in environment when applying a Left node");
            }
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            throw new NotImplementedException("Unable to convert partially compiled formula to z3 form. This formula contains a IOU expression. Try compiling to completely reify all IOU statements");
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                return new Below(ObjA, ObjB);
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            return toggleVars;
        }
    }

    public class ContainmentIr : Ir
    {
        public readonly ObjectVariable Container;
        public readonly ObjectVariable Contained;
        public RealExpr ComparisonVar;

        public ContainmentIr(ObjectVariable container, ObjectVariable contained, RealExpr comparisonVar, BoolExpr toggleVar) : base(toggleVar) {
            Container = container;
            Contained = contained;
            ComparisonVar = comparisonVar;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            if (env.ContainsKey(Container) && env.ContainsKey(Contained)) {
                BoundingBox boxA = env[Container].Item1;
                BoundingBox boxB = env[Contained].Item1;
                return new GeqIr(boxA.ContainmentFraction(boxB), ComparisonVar, ToggleVar);
            } else {
                throw new KeyNotFoundException("Unable to find keys in environment when applying an IOU node");
            }
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(1), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            throw new NotImplementedException("Unable to convert partially compiled formula to z3 form. This formula contains a IOU expression. Try compiling to completely reify all IOU statements");
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                return new Containment(Container, Contained, ((RatNum)z3Solution.Eval(ComparisonVar)).Double);
            } else {
                return null;
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 1));
            }
            return toggleVars;
        }
    }

    public class ColorComparisonIrApplied : Ir
    {
        public readonly YUV CompareTo;
        public readonly RealExpr Y;
        public readonly RealExpr U;
        public readonly RealExpr V;
        public readonly RealExpr Threshold;

        public ColorComparisonIrApplied(YUV compareTo, RealExpr y, RealExpr u, RealExpr v, RealExpr threshold, BoolExpr toggleVar) : base(toggleVar) {
            CompareTo = compareTo;
            Y = y;
            U = u;
            V = v;
            Threshold = threshold;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            return this;
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, 3));
            }
            return toggleVars;
        }

        public override BooleanAst? Compile(Model z3Solution) {
            throw new NotImplementedException("Cannot compile a ColorComparison to an AST representation");
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(3), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            var yCalc = CompareTo.Y - Y;
            var uCalc = CompareTo.U - U;
            var vCalc = CompareTo.V - V;
            BoolExpr passed = ctx.MkAnd(
                -Threshold <= yCalc, yCalc <= Threshold,
                -Threshold <= uCalc, uCalc <= Threshold,
                -Threshold <= vCalc, vCalc <= Threshold);

            optimize.Add(0 <= Y);
            optimize.Add(Y <= 1);
            optimize.Add(-0.5 <= U);
            optimize.Add(U <= 0.5);
            optimize.Add(-0.5 <= V);
            optimize.Add(V <= 0.5);
            optimize.Add(Threshold >= 0.1);

            if (form == Form.DNF) {
                return ctx.MkImplies(ToggleVar, passed);
            } else if (form == Form.CNF) {
                return ctx.MkAnd(ToggleVar, passed);
            }

            throw new NotImplementedException();
        }
    }

    public class ColorComparisonIr : Ir
    {
        public readonly ObjectVariable Obj;
        public RealExpr Y;
        public RealExpr U;
        public RealExpr V;
        public RealExpr Threshold;

        public const int ColorCost = 4;

        public ColorComparisonIr(ObjectVariable obj, RealExpr y, RealExpr u, RealExpr v, RealExpr threshold, BoolExpr toggleVar) : base(toggleVar) {
            Obj = obj;
            Y = y;
            U = u;
            V = v;
            Threshold = threshold;
        }

        public override Ir Apply(ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>> env, IOExample example) {
            if (env.ContainsKey(Obj)) {
                BoundingBox box = env[Obj].Item1;
                YUV boxColor = example.Resource.AverageColor(box);
                return new ColorComparisonIrApplied(boxColor, Y, U, V, Threshold, ToggleVar);
            } else {
                throw new KeyNotFoundException("Unable to find keys in environment when applying a color comparison node");
            }
        }

        public override List<Tuple<BoolExpr, uint>> CollectToggleVars(List<Tuple<BoolExpr, uint>> toggleVars) {
            if (ToggleVar != null) {
                toggleVars.Add(Tuple.Create<BoolExpr, uint>(ToggleVar, ColorCost));
            }
            return toggleVars;
        }

        public override BooleanAst? Compile(Model z3Solution) {
            if (ToggleVar == null || ToggleSolution(z3Solution)) {
                double solvedY = ((RatNum)z3Solution.Eval(Y)).Double;
                double solvedU = ((RatNum)z3Solution.Eval(U)).Double;
                double solvedV = ((RatNum)z3Solution.Eval(V)).Double;
                double solvedThreshold = ((RatNum)z3Solution.Eval(Threshold)).Double;
                return new ColorComparison(Obj, new YUV(solvedY, solvedU, solvedV), solvedThreshold);
            } else {
                return null;
            }
        }

        public override ArithExpr? ToggleVarSum(Context ctx) {
            if (ToggleVar != null) {
                return (ArithExpr)ctx.MkITE(ToggleVar, ctx.MkInt(ColorCost), ctx.MkInt(0));
            } else {
                return null;
            }
        }

        public override BoolExpr ToZ3(Optimize optimize, Context ctx, Form form) {
            throw new NotImplementedException("Unable to convert partially compiled formula to z3 form. This formula contains a color comparison expression. Try compiling to completely reify all color comparison statements");
        }
    }
}
 