using System;
using System.IO;
using System.ServiceModel.Configuration;

namespace Fixtures.Wcf.AmbiguousHostile
{
    public sealed class MarkerBehaviorExtension : BehaviorExtensionElement
    {
        public MarkerBehaviorExtension()
        {
            File.WriteAllText(
                "__DOMAINLENS_WCF_EXTENSION_MARKER__",
                "The custom WCF extension constructor executed.");
        }

        public override Type BehaviorType
        {
            get { return typeof(object); }
        }

        protected override object CreateBehavior()
        {
            return new object();
        }
    }
}
