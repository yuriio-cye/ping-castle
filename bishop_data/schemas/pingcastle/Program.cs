using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;

class Program
{
    static int counter = 1;
    static int seed = 1;
    static int itemsPerList = 2;

    static void Main(string[] args)
    {
        string pingCastlePath = "PingCastle.exe";
        string outputPath = "HealthcheckData-FILLED.xml";

        // Argument parsing
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--program":
                    if (++i < args.Length) pingCastlePath = args[i];
                    else { Usage("Missing value after --program"); return; }
                    break;

                case "--output":
                    if (++i < args.Length) outputPath = args[i];
                    else { Usage("Missing value after --output"); return; }
                    break;

                case "--seed":
                    if (++i < args.Length && int.TryParse(args[i], out seed))
                        counter = seed;
                    else { Usage("Invalid value after --seed"); return; }
                    break;

                case "--items-per-list":
                    if (++i < args.Length && int.TryParse(args[i], out itemsPerList))
                        itemsPerList = Math.Max(1, itemsPerList);
                    else { Usage("Invalid value after --items-per-list"); return; }
                    break;

                case "--help":
                case "-h":
                    Usage();
                    return;

                default:
                    Usage("Unknown argument: " + args[i]);
                    return;
            }
        }

        if (!File.Exists(pingCastlePath))
        {
            Console.Error.WriteLine("File not found: " + pingCastlePath);
            Environment.Exit(1);
        }

        var asm = Assembly.LoadFrom(pingCastlePath);
        var type = asm.GetType("PingCastle.Healthcheck.HealthcheckData");

        if (type == null)
        {
            Console.Error.WriteLine("Type PingCastle.Healthcheck.HealthcheckData not found.");
            Environment.Exit(1);
        }

        object instance = Activator.CreateInstance(type);
        Populate(instance);

        var serializer = new XmlSerializer(type);
        using var writer = new StreamWriter(outputPath);
        serializer.Serialize(writer, instance);

        Console.WriteLine("XML written to: " + outputPath);
    }

    static void Populate(object obj, HashSet<Type> visited = null)
    {
        if (obj == null) return;
        visited ??= new HashSet<Type>();
        Type type = obj.GetType();
        if (visited.Contains(type)) return;
        visited.Add(type);

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanWrite) continue;

            var propType = prop.PropertyType;

            try
            {
                if (propType == typeof(string))
                {
                    prop.SetValue(obj, prop.Name + "_" + counter++);
                }
                else if (propType == typeof(int) || propType == typeof(long))
                {
                    prop.SetValue(obj, counter++);
                }
                else if (propType == typeof(bool))
                {
                    prop.SetValue(obj, true);
                }
                else if (propType == typeof(DateTime))
                {
                    prop.SetValue(obj, DateTime.UtcNow.AddDays(-counter));
                }
                else if (propType.IsEnum)
                {
                    var enumValues = Enum.GetValues(propType);
                    if (enumValues.Length > 0)
                        prop.SetValue(obj, enumValues.GetValue(0));
                }
                else if (typeof(IEnumerable).IsAssignableFrom(propType) && propType != typeof(string))
                {
                    Type itemType = GetEnumerableItemType(propType);
                    if (itemType != null && itemType.GetConstructor(Type.EmptyTypes) != null)
                    {
                        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
                        for (int i = 0; i < itemsPerList; i++)
                        {
                            var item = Activator.CreateInstance(itemType);
                            Populate(item, new HashSet<Type>(visited));
                            list.Add(item);
                        }
                        prop.SetValue(obj, list);
                    }
                }
                else if (propType.IsClass && propType.GetConstructor(Type.EmptyTypes) != null)
                {
                    var nested = Activator.CreateInstance(propType);
                    Populate(nested, new HashSet<Type>(visited));
                    prop.SetValue(obj, nested);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Skipped property " + prop.Name + " (" + propType.Name + "): " + ex.Message);
            }
        }
    }

    static Type GetEnumerableItemType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType)
        {
            Type[] args = type.GetGenericArguments();
            if (args.Length == 1)
                return args[0];
        }

        return type.GetInterfaces()
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(t => t.GetGenericArguments()[0])
            .FirstOrDefault();
    }

    static void Usage(string error = null)
    {
        if (!string.IsNullOrEmpty(error))
            Console.Error.WriteLine(error);

        Console.WriteLine(@"
Usage:
  Program.exe [--program PingCastle.exe] [--output healthcheck.xml]
              [--seed 123] [--items-per-list 2]

Options:
  --program          Path to PingCastle.exe (default: PingCastle.exe)
  --output           Output XML file path (default: HealthcheckData-FILLED.xml)
  --seed             Initial seed value for deterministic output (default: 1)
  --items-per-list   Number of items to generate in each List<T> (default: 2)
  --help             Show this help message

Example:
  Program.exe --program ./PingCastle.exe --output out.xml --seed 100 --items-per-list 3
");
    }
}
