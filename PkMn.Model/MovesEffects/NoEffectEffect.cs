﻿using System.Linq;
using System.Xml;
using PkMn.Model.Enums;

namespace PkMn.Model.MoveEffects
{
    public class NoEffectEffect : MoveEffect
    {
        public readonly Element Condition;

        protected override string[] ValidAttributes { get { return base.ValidAttributes.Union(new string[] { "condition" }).ToArray(); } }

        public NoEffectEffect(MoveEffectType type, XmlNode node)
            : base(type, node)
        {
            Condition = Element.Elements[node.Attributes["condition"].Value];
        }

        public override string ToString()
        {
            return string.Format("Move will not affect {0} types", Condition.Name.ToLower());
        }
    }
}
