using System;

namespace DISourceGen.ExampleConsoleApp
{
    internal class Program
    {
        internal static void Main()
        {
            Console.WriteLine("Types in Assembly");
            foreach (Type t in typeof(Program).Assembly.GetTypes())
            {
                Console.WriteLine(t.FullName);
            }

            Console.WriteLine("\n\n" + DI.Services.Resolve<IFoo>().Name);
            Console.WriteLine(DI.Services.Resolve<IBar>().Number);
        }
    }

    public interface IFoo
    {
        public string Name { get; }
    }

    public class Foo : IFoo
    {
        public string Name { get; } = "FooOutput";
    }

    public interface IBase
    {
        public string Output { get; }
    }

    public class Based : IBase
    {
        public string Output { get; }

        public Based(IFoo foo)
        {
            Output = foo.Name + "DoDo";
        }
    }

    [DI.Transient]
    public interface IBar
    {
        public int Number { get; }
    }

    public class Bar : IBar
    {
        public int Number { get; }
        
        public Bar()
        {

        }

        [DI.PrimaryConstructor]
        public Bar(IFoo foo, IBase @base)
        {
            Number = $"{foo.Name}{@base.Output}".Length;
        }
    }
}
