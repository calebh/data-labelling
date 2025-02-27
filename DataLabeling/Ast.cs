﻿using DataLabeling.Color;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLabeling
{
    public abstract class Ast
    {

    }

    public class ProgramAst : Ast
    {
        public List<LabelApply> ApplyList { get; private set; }

        public ProgramAst(List<LabelApply> applyList) {
            ApplyList = applyList;
        }

        public override string ToString() {
            return String.Format("Program({0})", String.Join(", ", ApplyList.Select(m => m.ToString())));
        }
    }

    public class LabelApply : Ast
    {
        public ObjectLiteral Action { get; private set; }
        public ObjectList ObjectList { get; private set; }

        public LabelApply(ObjectLiteral action, ObjectList objectList) {
            Action = action;
            ObjectList = objectList;
        }

        public override string ToString() {
            return String.Format("LabelApply({0}, {1})", Action.ToString(), ObjectList.ToString());
        }
    }

    public class GroupApply : Ast
    {
        public GroupLiteral Action { get; private set; }
        public ObjectList ObjectList { get; private set; }

        public GroupApply(GroupLiteral action, ObjectList objectList) {
            Action = action;
            ObjectList = objectList;
        }

        public override string ToString() {
            return String.Format("GroupApply({0}, {1})", Action.ToString(), ObjectList.ToString());
        }
    }

    public abstract class ObjectList : Ast
    {

    }

    public class AllObjects : ObjectList
    {
        public AllObjects() { }

        public override string ToString() {
            return "AllObjects()";
        }
    }

    public class Filter : ObjectList
    {
        public PredicateLambda Predicate { get; private set; }
        public ObjectList ObjectList { get; private set; }

        public Filter(PredicateLambda predicate, ObjectList objectList) {
            Predicate = predicate;
            ObjectList = objectList;
        }

        public override string ToString() {
            return String.Format("Filter({0}, {1})", Predicate.ToString(), ObjectList.ToString());
        }
    }

    public abstract class ObjectAst : Ast
    {

    }

    public class ObjectLiteral : ObjectAst
    {
        public string LabelName { get; private set; }

        public ObjectLiteral(string labelName) {
            LabelName = labelName;
        }

        public override bool Equals(object? obj) {
            ObjectLiteral other = obj as ObjectLiteral;
            if (other == null) {
                return false;
            } else {
                return LabelName.Equals(other.LabelName);
            }
        }

        public override int GetHashCode() {
            return LabelName.GetHashCode();
        }

        public override string ToString() {
            return String.Format("\"{0}\"", LabelName);
        }
    }

    public class GroupLiteral
    {
        public string LabelName { get; private set; }

        public GroupLiteral(string labelName) {
            LabelName = labelName;
        }

        public override bool Equals(object? obj) {
            GroupLiteral other = obj as GroupLiteral;
            if (other == null) {
                return false;
            } else {
                return LabelName.Equals(other.LabelName);
            }
        }

        public override int GetHashCode() {
            return LabelName.GetHashCode();
        }

        public override string ToString() {
            return String.Format("\"{0}\"", LabelName);
        }
    }

    public class ObjectVariable : ObjectAst
    {
        public string VariableName { get; private set; }

        public ObjectVariable(string variableName) {
            VariableName = variableName;
        }

        public override int GetHashCode() {
            return VariableName.GetHashCode();
        }

        public override bool Equals(object? obj) {
            return Equals(obj as ObjectVariable);
        }

        public bool Equals(ObjectVariable other) {
            return other != null && VariableName == other.VariableName;
        }

        public override string ToString() {
            return VariableName;
        }
    }

    public class PredicateLambda : Ast
    {
        public ObjectVariable VarName { get; private set; }
        public BooleanAst Body { get; private set; }

        public PredicateLambda(ObjectVariable varName, BooleanAst body) {
            VarName = varName;
            Body = body;
        }

        public override string ToString() {
            return string.Format("fun {0} -> {1}", VarName.ToString(), Body.ToString());
        }
    }

    public abstract class BooleanAst : Ast {
    
    }

    public class TrueBool : BooleanAst
    {
        public TrueBool() { }

        public override string ToString() {
            return "true";
        }
    }

    public class FalseBool : BooleanAst
    {
        public FalseBool() { }

        public override string ToString() {
            return "false";
        }
    }

    public class LabelIs : BooleanAst
    {
        public readonly ObjectVariable ObjVar;
        public readonly ObjectLiteral ObjLit;

        public LabelIs(ObjectVariable objVar, ObjectLiteral objLit) {
            ObjVar = objVar;
            ObjLit = objLit;
        }

        public override string ToString() {
            return string.Format("LabelIs({0}, {1})", ObjVar.ToString(), ObjLit.ToString());
        }
    }

    public class EqualLabel : BooleanAst
    {
        public readonly ObjectVariable ObjectA;
        public readonly ObjectVariable ObjectB;

        public EqualLabel(ObjectVariable objA, ObjectVariable objB) {
            ObjectA = objA;
            ObjectB = objB;
        }

        public override string ToString() {
            return string.Format("EqualLabel({0}, {1})", ObjectA.ToString(), ObjectB.ToString());
        }
    }

    public class NotBool : BooleanAst
    {
        public BooleanAst Inner { get; private set; }

        public NotBool(BooleanAst inner) {
            Inner = inner;
        }

        public override string ToString() {
            return string.Format("!{0}", Inner.ToString());
        }
    }

    public class OrBool : BooleanAst
    {
        public BooleanAst Left { get; private set; }
        public BooleanAst Right { get; private set; }

        public OrBool(BooleanAst left, BooleanAst right) {
            Left = left;
            Right = right;
        }

        public override string ToString() {
            return string.Format("({0} || {1})", Left.ToString(), Right.ToString());
        }
    }

    public class AndBool : BooleanAst
    {
        public BooleanAst Left { get; private set; }
        public BooleanAst Right { get; private set; }

        public AndBool(BooleanAst left, BooleanAst right) {
            Left = left;
            Right = right;
        }

        public override string ToString() {
            return string.Format("({0} && {1})", Left.ToString(), Right.ToString());
        }
    }

    public class Exists : BooleanAst
    {
        public ObjectVariable VarName { get; private set; }
        public BooleanAst Body { get; private set; }

        public Exists(ObjectVariable varName, BooleanAst body) {
            VarName = varName;
            Body = body;
        }

        public override string ToString() {
            return string.Format("exists {0} . ({1})", VarName.ToString(), Body.ToString());
        }
    }

    public class Forall : BooleanAst
    {
        public ObjectVariable VarName { get; private set; }
        public BooleanAst Body { get; private set; }

        public Forall(ObjectVariable varName, BooleanAst body) {
            VarName = varName;
            Body = body;
        }

        public override string ToString() {
            return string.Format("forall {0} . ({1})", VarName.ToString(), Body.ToString());
        }
    }

    public class Left : BooleanAst
    {
        public ObjectAst ObjectA { get; private set; }
        public ObjectAst ObjectB { get; private set; }

        public Left(ObjectAst objectA, ObjectAst objectB) {
            ObjectA = objectA;
            ObjectB = objectB;
        }

        public override string ToString() {
            return string.Format("Left({0}, {1})", ObjectA.ToString(), ObjectB.ToString());
        }
    }

    public class Right : BooleanAst
    {
        public ObjectAst ObjectA { get; private set; }
        public ObjectAst ObjectB { get; private set; }

        public Right(ObjectAst objectA, ObjectAst objectB) {
            ObjectA = objectA;
            ObjectB = objectB;
        }

        public override string ToString() {
            return string.Format("Right({0}, {1})", ObjectA.ToString(), ObjectB.ToString());
        }
    }

    public class Above : BooleanAst
    {
        public ObjectAst ObjectA { get; private set; }
        public ObjectAst ObjectB { get; private set; }

        public Above(ObjectAst objectA, ObjectAst objectB) {
            ObjectA = objectA;
            ObjectB = objectB;
        }

        public override string ToString() {
            return string.Format("Above({0}, {1})", ObjectA.ToString(), ObjectB.ToString());
        }
    }

    public class Below : BooleanAst
    {
        public ObjectAst ObjectA { get; private set; }
        public ObjectAst ObjectB { get; private set; }

        public Below(ObjectAst objectA, ObjectAst objectB) {
            ObjectA = objectA;
            ObjectB = objectB;
        }

        public override string ToString() {
            return string.Format("Below({0}, {1})", ObjectA.ToString(), ObjectB.ToString());
        }
    }

    public class IOU : BooleanAst
    {
        public ObjectAst ObjectA { get; private set; }
        public ObjectAst ObjectB { get; private set; }
        public double Threshold { get; private set; }

        public IOU(ObjectAst objectA, ObjectAst objectB, double threshold) {
            ObjectA = objectA;
            ObjectB = objectB;
            Threshold = threshold;
        }

        public override string ToString() {
            return string.Format("IOU({0}, {1}) >= {2}", ObjectA.ToString(), ObjectB.ToString(), Threshold);
        }
    }

    public class Containment : BooleanAst
    {
        public readonly ObjectAst Container;
        public readonly ObjectAst Contained;
        public readonly double Threshold;

        public Containment(ObjectAst container, ObjectAst contained, double threshold) {
            Container = container;
            Contained = contained;
            Threshold = threshold;
        }

        public override string ToString() {
            return string.Format("Containment({0}, {1}) >= {2}", Container.ToString(), Contained.ToString(), Threshold);
        }
    }

    public class ColorComparison : BooleanAst
    {
        public readonly ObjectAst Obj;
        public readonly YUV Color;
        public readonly double Threshold;

        public ColorComparison(ObjectAst obj, YUV color, double threshold) {
            Obj = obj;
            Color = color;
            Threshold = threshold;
        }

        public override string ToString() {
            RGB rgbColor = new RGB(Color);
            return string.Format("ColorContainment({0}, ({1}, {2}, {3}), {4})", Obj.ToString(), (int) (rgbColor.R * 255.0), (int) (rgbColor.G * 255.0), (int) (rgbColor.B * 255.0), Threshold);
        }
    }
}
