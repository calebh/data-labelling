﻿using Microsoft.Z3;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLabeling
{
    public struct SynthesisConfig
    {
        public bool UseColorSynthesis;
        public bool UsePlacementSynthesis;
        public bool UseContainmentSynthesis;
    }

    public static class Synthesize
    {
        private static List<ObjectLiteral> GenerateBaseLibrary(List<IOExample> examples) {
            HashSet<ObjectLiteral> result = new HashSet<ObjectLiteral>();
            foreach (IOExample example in examples) {
                foreach (BoundingBox b in example.GetBoxes()) {
                    result.Add(example.GetBase(b));
                }
            }
            return result.ToList();
        }

        private static List<ObjectLiteral> GeneratePreciseLibrary(List<IOExample> examples) {
            HashSet<ObjectLiteral> result = new HashSet<ObjectLiteral>();
            foreach (IOExample example in examples) {
                foreach (BoundingBox b in example.GetBoxes()) {
                    result.UnionWith(example.GetPrecise(b));
                }
            }
            return result.ToList();
        }

        private static List<GroupLiteral> GenerateGroupLibrary(List<IOExample> examples) {
            HashSet<GroupLiteral> result = new HashSet<GroupLiteral>();
            foreach (IOExample example in examples) {
                foreach (BoundingBox b in example.GetBoxes()) {
                    result.UnionWith(example.GetGroups(b));
                }
            }
            return result.ToList();
        }

        public static Tuple<List<List<LabelApply>>, List<List<GroupApply>>> DoSynthesis(List<IOExample> examples, SynthesisConfig config) {
            List<ObjectLiteral> baseLibrary = GenerateBaseLibrary(examples);
            List<ObjectLiteral> preciseLibrary = GeneratePreciseLibrary(examples);

            List<List<LabelApply>> labelApplies = new List<List<LabelApply>>();

            foreach (ObjectLiteral preciseLabel in preciseLibrary) {
                List<LabelApply> synthResult =
                    RunSynthesis(examples, config,
                        baseLibrary, preciseLabel,
                        (ObjectLiteral label, Filter f) => new LabelApply(label, f),
                        (IOExample example, BoundingBox bbox, ObjectLiteral label) => example.GetPrecise(bbox).Contains(label));
                labelApplies.Add(synthResult);
            }

            List<GroupLiteral> groupLibrary = GenerateGroupLibrary(examples);

            List<List<GroupApply>> groupApplies = new List<List<GroupApply>>();

            foreach (GroupLiteral groupLabel in groupLibrary) {
                List<GroupApply> synthResult =
                    RunSynthesis(examples, config,
                        baseLibrary, groupLabel,
                        (GroupLiteral label, Filter f) => new GroupApply(label, f),
                        (IOExample example, BoundingBox bbox, GroupLiteral label) => example.GetGroups(bbox).Contains(label));
                groupApplies.Add(synthResult);
            }

            return Tuple.Create(labelApplies, groupApplies);
        }

        private static List<T> RunSynthesis<T, U>(List<IOExample> examples, SynthesisConfig config, List<ObjectLiteral> baseLibrary, U preciseLabel, Func<U, Filter, T> newT, Func<IOExample, BoundingBox, U, bool> isActionApplied) {
            using (Context ctx = new Context(new Dictionary<string, string>() { { "model", "true" } })) {
                int varIndex = 0;
                Func<BoolExpr> getFreshToggleVar = () => {
                    BoolExpr ret = ctx.MkBoolConst("v" + varIndex.ToString());
                    varIndex++;
                    return ret;
                };

                Func<RealExpr> getFreshRealVar = () => {
                    RealExpr ret = ctx.MkRealConst("r" + varIndex.ToString());
                    varIndex++;
                    return ret;
                };

                List<T> synthesizedMaps = new List<T>();

                List<int> numClausesPerQuantLevel = new List<int> { 5, 5, 5 };
                int quantifierNestedLevel = 1;

                bool incrementedQuantifierLevel = true;

                while (true) {
                    int numClauses = numClausesPerQuantLevel[quantifierNestedLevel];

                    Func<int, List<ObjectVariable>, Tuple<Ir, Ir>> recursivelyGenerate = null;
                    recursivelyGenerate = (int level, List<ObjectVariable> accumLevels) => {
                        ObjectVariable levelVar = new ObjectVariable("x" + level.ToString());
                        ObjectVariable nextLevelVar = new ObjectVariable("x" + (level - 1).ToString());
                        List<ObjectVariable> nextAccumLevel = new List<ObjectVariable>(accumLevels);
                        nextAccumLevel.Add(nextLevelVar);

                        List<Ir> clausesDnf = new List<Ir>();
                        List<Ir> clausesCnf = new List<Ir>();

                        for (int clauseI = 0; clauseI < numClauses; clauseI++) {
                            List<Ir> clauseDnf = new List<Ir>();
                            List<Ir> clauseCnf = new List<Ir>();

                            foreach (ObjectLiteral label in baseLibrary) {
                                clauseDnf.Add(new LabelIsIr(levelVar, label, false, getFreshToggleVar()));
                                clauseCnf.Add(new LabelIsIr(levelVar, label, false, getFreshToggleVar()));
                                clauseDnf.Add(new LabelIsIr(levelVar, label, true, getFreshToggleVar()));
                                clauseCnf.Add(new LabelIsIr(levelVar, label, true, getFreshToggleVar()));
                            }

                            if (config.UseColorSynthesis) {
                                clauseDnf.Add(new ColorComparisonIr(levelVar, getFreshRealVar(), getFreshRealVar(), getFreshRealVar(), getFreshRealVar(), getFreshToggleVar()));
                                clauseCnf.Add(new ColorComparisonIr(levelVar, getFreshRealVar(), getFreshRealVar(), getFreshRealVar(), getFreshRealVar(), getFreshToggleVar()));
                            }

                            for (int i = 0; i < accumLevels.Count; i++) {
                                for (int j = i + 1; j < accumLevels.Count; j++) {
                                    clauseDnf.Add(new EqualLabelIr(accumLevels[i], accumLevels[j], false, getFreshToggleVar()));
                                    clauseCnf.Add(new EqualLabelIr(accumLevels[i], accumLevels[j], false, getFreshToggleVar()));
                                    clauseDnf.Add(new EqualLabelIr(accumLevels[i], accumLevels[j], true, getFreshToggleVar()));
                                    clauseCnf.Add(new EqualLabelIr(accumLevels[i], accumLevels[j], true, getFreshToggleVar()));
                                    if (config.UseContainmentSynthesis) {
                                        clauseDnf.Add(new IOUIr(accumLevels[i], accumLevels[j], getFreshRealVar(), getFreshToggleVar()));
                                        clauseCnf.Add(new IOUIr(accumLevels[i], accumLevels[j], getFreshRealVar(), getFreshToggleVar()));
                                        clauseDnf.Add(new ContainmentIr(accumLevels[i], accumLevels[j], getFreshRealVar(), getFreshToggleVar()));
                                        clauseCnf.Add(new ContainmentIr(accumLevels[i], accumLevels[j], getFreshRealVar(), getFreshToggleVar()));
                                        clauseDnf.Add(new ContainmentIr(accumLevels[j], accumLevels[i], getFreshRealVar(), getFreshToggleVar()));
                                        clauseCnf.Add(new ContainmentIr(accumLevels[j], accumLevels[i], getFreshRealVar(), getFreshToggleVar()));
                                    }
                                    if (config.UsePlacementSynthesis) {
                                        clauseDnf.Add(new LeftIr(accumLevels[i], accumLevels[j], getFreshToggleVar()));
                                        clauseCnf.Add(new LeftIr(accumLevels[i], accumLevels[j], getFreshToggleVar()));
                                        clauseDnf.Add(new RightIr(accumLevels[i], accumLevels[j], getFreshToggleVar()));
                                        clauseCnf.Add(new RightIr(accumLevels[i], accumLevels[j], getFreshToggleVar()));
                                        clauseDnf.Add(new BelowIr(accumLevels[i], accumLevels[j], getFreshToggleVar()));
                                        clauseCnf.Add(new BelowIr(accumLevels[i], accumLevels[j], getFreshToggleVar()));
                                        clauseDnf.Add(new AboveIr(accumLevels[i], accumLevels[j], getFreshToggleVar()));
                                        clauseCnf.Add(new AboveIr(accumLevels[i], accumLevels[j], getFreshToggleVar()));
                                    }
                                }
                            }

                            if (level > 0) {
                                Tuple<Ir, Ir> recResult = recursivelyGenerate(level - 1, nextAccumLevel);
                                Ir dnfRes = recResult.Item1;
                                Ir cnfRes = recResult.Item2;

                                clauseDnf.Add(new AnyIr(nextLevelVar, dnfRes, getFreshToggleVar()));
                                clauseDnf.Add(new AllIr(nextLevelVar, dnfRes, getFreshToggleVar()));
                                clauseCnf.Add(new AnyIr(nextLevelVar, cnfRes, getFreshToggleVar()));
                                clauseCnf.Add(new AllIr(nextLevelVar, cnfRes, getFreshToggleVar()));
                            }

                            clausesDnf.Add(new AndIr(clauseDnf, null));
                            clausesCnf.Add(new OrIr(clauseCnf, null));
                        }

                        Ir dnf = new OrIr(clausesDnf, null);
                        Ir cnf = new AndIr(clausesCnf, null);

                        return Tuple.Create(dnf, cnf);
                    };

                    ObjectVariable outermostVariable = new ObjectVariable("x" + quantifierNestedLevel.ToString());

                    Tuple<Ir, Ir> irResult = recursivelyGenerate(quantifierNestedLevel, new List<ObjectVariable>() { outermostVariable });
                    Ir dnf = irResult.Item1;
                    Ir cnf = irResult.Item2;

                    Optimize s_dnf = ctx.MkOptimize();
                    Optimize s_cnf = ctx.MkOptimize();

                    foreach (IOExample example in examples) {
                        foreach (BoundingBox box in example.GetBoxes()) {
                            var initEnv = ImmutableDictionary<ObjectVariable, Tuple<BoundingBox, ObjectLiteral>>.Empty.Add(outermostVariable, Tuple.Create(box, example.GetBase(box)));
                            var compiled_dnf_0 = dnf.Apply(initEnv, example);
                            var compiled_dnf = compiled_dnf_0.ToZ3(s_dnf, ctx, Form.DNF);
                            var compiled_cnf_0 = cnf.Apply(initEnv, example);
                            var compiled_cnf = compiled_cnf_0.ToZ3(s_cnf, ctx, Form.CNF);
                            bool actionApplied = isActionApplied(example, box, preciseLabel);
                            BoolExpr actionAppliedZ3 = ctx.MkBool(actionApplied);
                            s_dnf.Add(ctx.MkIff(compiled_dnf, actionAppliedZ3));
                            s_cnf.Add(ctx.MkIff(compiled_cnf, actionAppliedZ3));
                        }
                    }

                    int smallestObjective = int.MaxValue;

                    List<Tuple<BoolExpr, uint>> toggleVarsDnf = dnf.CollectToggleVars();
                    foreach (Tuple<BoolExpr, uint> tvWithWeight in toggleVarsDnf) {
                        s_dnf.AssertSoft(ctx.MkNot(tvWithWeight.Item1), tvWithWeight.Item2, "tv");
                    }

                    List<Tuple<BoolExpr, uint>> toggleVarsCnf = cnf.CollectToggleVars();
                    foreach (Tuple<BoolExpr, uint> tvWithWeight in toggleVarsCnf) {
                        s_cnf.AssertSoft(ctx.MkNot(tvWithWeight.Item1), tvWithWeight.Item2, "tv");
                    }

                    List<T> bestPrograms = new List<T>();

                    Console.WriteLine("Synthesizing with " + numClauses + " at " + quantifierNestedLevel + " deep");

                    Action<Optimize, Ir, List<BoolExpr>> runZ3 = (Optimize s, Ir nf, List<BoolExpr> toggleVars) => {
                        while (bestPrograms.Count < 5) {
                            if (s.Check() == Status.SATISFIABLE) {
                                Model m = s.Model;
                                int objectiveValue = ((IntNum)m.Eval(nf.ToggleVarSum(ctx)).Simplify()).Int;
                                if (objectiveValue <= smallestObjective) {
                                    smallestObjective = objectiveValue;
                                    BooleanAst? synthesizedPred = nf.Compile(m);
                                    bestPrograms.Add(newT(preciseLabel, new Filter(new PredicateLambda(outermostVariable, synthesizedPred), new AllObjects())));
                                    // Add the constraint that we are looking for a solution that is different from the one we just found
                                    s.Add(ctx.MkOr(toggleVars.Where(tv => m.Eval(tv).BoolValue == Z3_lbool.Z3_L_TRUE).Select(tv => ctx.MkIff(tv, ctx.MkFalse())).ToArray()));
                                } else {
                                    break;
                                }
                            } else {
                                break;
                            }
                        }
                    };

                    runZ3(s_dnf, dnf, toggleVarsDnf.Select(t => t.Item1).ToList());
                    runZ3(s_cnf, cnf, toggleVarsCnf.Select(t => t.Item1).ToList());

                    if (bestPrograms.Count > 0) {
                        return bestPrograms;
                    }

                    numClausesPerQuantLevel[quantifierNestedLevel] += 2;
                    quantifierNestedLevel++;
                    if (quantifierNestedLevel == numClausesPerQuantLevel.Count) {
                        if (!incrementedQuantifierLevel) {
                            numClausesPerQuantLevel.Add(5);
                            incrementedQuantifierLevel = true;
                        } else {
                            quantifierNestedLevel = 0;
                            incrementedQuantifierLevel = false;
                        }
                    }
                }
            }
        }
    }
}
