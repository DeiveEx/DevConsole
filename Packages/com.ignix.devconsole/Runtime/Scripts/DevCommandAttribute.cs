using System;

namespace Ignix.Debug.Console
{
    public class DevCommandAttribute : Attribute
    {
        public string name;
        public string description;
        public string category;
        public TargetType targetType;

        public DevCommandAttribute(string name = null, string description = null, string category = null, TargetType targetType = TargetType.Single)
        {
            this.name = name;
            this.description = description;
            this.category = category;
            this.targetType = targetType;
        }
    }
}

