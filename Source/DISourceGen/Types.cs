using System;
using System.Collections.Generic;
using System.Text;

namespace DISourceGen
{
    internal static class Types
    {
        internal const string ServicesStub = @"
namespace DI
{
    public static class Services
    {
        public static T Resolve<T>()
        {
            return default;
        }
    }
}";

        internal const string TransientAttribute = @"
using System;

namespace DI
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class TransientAttribute : Attribute { }
}";

        internal const string PrimaryConstructorAttribute = @"
using System;

namespace DI
{
    [AttributeUsage(AttributeTargets.Constructor)]
    public class PrimaryConstructorAttribute : Attribute { }
}";

//        internal const string InjectAttribute = @"
//using System;

//namespace DI
//{
//    [AttributeUsage(AttributeTargets.Class)]
//    public class InjectAttribute : Attribute
//    {
//        public string FullTypePath { get; set; } = null;
//        public string PropertyName { get; set; } = null;

//        //public InjectAttribute(Type type)
//        //{
//        //    FullTypePath = type.FullName;
//        //}
//    }
//}";
    }
}
