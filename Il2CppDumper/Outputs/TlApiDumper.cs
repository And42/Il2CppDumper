using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppDumper
{
    public class TlApiDumper
    {
        private const string HEADER = 
            @"// unity primitives
            using un_bool   = int8_t;
            using un_sbyte  = int8_t;
            using un_byte   = uint8_t;
            using un_short  = int16_t;
            using un_ushort = uint16_t;
            using un_int    = int32_t;
            using un_uint   = uint32_t;
            using un_long   = int64_t;
            using un_ulong  = uint64_t;
            using un_float  = float;
            using un_double = double;

            // common unity types 
            using un_string = System_String_o;";

        public void DumpHeader(TextWriter writer, string indentation)
        {
            writer.Write(string.Join(
                Environment.NewLine,
                HEADER
                    .Split(new[] {Environment.NewLine}, StringSplitOptions.None)
                    .Select(it => indentation + it.Trim())
            ));
        }
        
        public void Dump(ScriptGenerator.ImageInfo image, TextWriter writer, string indentation)
        {
            foreach (ScriptGenerator.TypeInfo type in image.Types)
            {
                Dump(type, writer, indentation);
                writer.WriteLine();
                writer.WriteLine();
            }
        }
        
        public void Dump(ScriptGenerator.TypeInfo type, TextWriter writer, string indentation)
        {
            string innerIndentation = indentation + "    ";

            if (type.DeclaringClass == null)
            {
                string @namespace = string.Join(
                    "::",
                    type.FullName
                        .Split('.')
                        .Select(ScriptGenerator.FixName)
                );
                writer.WriteLine($"{indentation}namespace {@namespace} {{");
                writer.Write($"{innerIndentation}inline");
                writer.WriteLine($" Il2CppClassLoader classLoader(\"{type.Namespace}\", \"{type.Name}\");");
            }
            else
            {
                var currentType = type;
                var tree = new List<ScriptGenerator.TypeInfo>();
                while (currentType != null)
                {
                    tree.Add(currentType);
                    currentType = currentType.DeclaringClass;
                }
                tree.Reverse();
                // namespace is set only in the top level class; so, use it
                string classesTree = string.Join("::", tree.Select(it => ScriptGenerator.FixName(it.Name)));
                string @namespace = string.Join(
                    "::",
                    tree[0].Namespace
                        .Split('.')
                        .Select(ScriptGenerator.FixName)
                ) + "::" + classesTree;
                writer.WriteLine($"{indentation}namespace {@namespace} {{");
                writer.Write($"{innerIndentation}inline");
                var treeString = string.Join(", ", tree.Select(it => $"\"{it.Name}\""));
                writer.WriteLine($" Il2CppNestedClassLoader classLoader(\"{tree[0].Namespace}\", {{{treeString}}});");
            }
            writer.WriteLine();

            void DumpItems<T>(Action<T, TextWriter, string> dumper, ICollection<T> items, bool addSpacer)
            {
                foreach (T item in items)
                {
                    dumper(item, writer, innerIndentation);
                    writer.WriteLine();
                }
                if (addSpacer && items.Count > 0)
                    writer.WriteLine();
            }
            
            void DumpUniqueItems<T>(Action<T, string, TextWriter, string> dumper, IDictionary<T, string> unique, ICollection<T> items, bool addSpacer)
            {
                foreach (T item in items)
                {
                    dumper(item, unique[item], writer, innerIndentation);
                    writer.WriteLine();
                }
                if (addSpacer && items.Count > 0)
                    writer.WriteLine();
            }

            List<ScriptGenerator.MethodInfo> SortMethodsByCSharpSignature(List<ScriptGenerator.MethodInfo> methods)
            {
                return methods.Select(it =>
                {
                    string tempSignature;
                    using (var tempWriter = new StringWriter())
                    {
                        DumpCSharpComment(it, tempWriter, string.Empty);
                        tempSignature = tempWriter.ToString();
                    }
                    return new KeyValuePair<ScriptGenerator.MethodInfo, string>(it, tempSignature);
                })
                .OrderBy(it => it.Value)
                .Select(it => it.Key)
                .ToList();
            }
            
            // sorting fields so that if in future versions fields' ordering changes
            // fields' ordering in api stays the same
            var sortedFields = type.Fields.OrderBy(it => it.Name).ToList();
            var instanceFields = sortedFields.Where(it => !it.Static).ToList();
            var staticFields = sortedFields.Where(it => it.Static).ToList();
            // sorting methods so that if in future versions methods' ordering changes
            // methods' ordering in api stays the same
            var sortedMethods = SortMethodsByCSharpSignature(type.Methods);
            var instanceMethods = sortedMethods.Where(it => !it.Static).ToList();
            var staticMethods = sortedMethods.Where(it => it.Static).ToList();
            
            DumpItems(Dump, instanceFields, true);
            DumpItems(Dump, staticFields, true);
            
            // all function holders' names are in format "<name>_<number_of_parameters>{_<same_parameters_index>}"
            // "<same_parameters_index>" is optional and is used only if there are 2 or more functions with the same
            // "<name>_<number_of_parameters>" part
            var uniqueNames = new Dictionary<ScriptGenerator.MethodInfo, string>();
            var fixedNames = new List<(ScriptGenerator.MethodInfo method, string name)>();
            foreach (ScriptGenerator.MethodInfo method in type.Methods)
                fixedNames.Add((method, $"{ScriptGenerator.FixName(method.Name)}_{method.Parameters.Count}"));
            foreach (var group in fixedNames.GroupBy(it => it.name))
            {
                var groupItems = group.ToList();
                if (groupItems.Count == 1)
                    uniqueNames[groupItems[0].method] = group.Key;
                else
                {
                    // need to sort again as grouping does not preserve order
                    var sorted = SortMethodsByCSharpSignature(groupItems.ConvertAll(it => it.method));
                    for (int i = 0; i < sorted.Count; i++)
                        uniqueNames[sorted[i]] = $"{group.Key}_{i + 1}";
                }
            }

            DumpUniqueItems(Dump, uniqueNames, instanceMethods, true);
            DumpUniqueItems(Dump, uniqueNames, staticMethods, false);

            writer.Write($"{indentation}}}");
        }
        
        public void Dump(ScriptGenerator.FieldInfo field, TextWriter writer, string indentation)
        {
            // example: "    // static string SomeField"
            writer.WriteLine(
                "{0}// {1}{2} {3}",
                indentation, field.Static ? "static " : "", field.Type.FullCSharpName, field.Name
            );
            
            // example: "    inline StaticField<string> SomeField(classLoader, "SomeField");"
            writer.Write(
                "{0}inline {1}Field<{2}> {3}(classLoader, \"{4}\");",
                indentation, field.Static ? "Static" : "Instance", field.Type.FullNativeName, field.Name, field.Name
            );
        }
        
        public void Dump(ScriptGenerator.MethodInfo method, string uniqueMethodName, TextWriter writer, string indentation)
        {
            DumpCSharpComment(method, writer, indentation);
            writer.WriteLine();
            DumpNativeHolder(method, uniqueMethodName, writer, indentation);
        }
        
        public void DumpCSharpComment(ScriptGenerator.MethodInfo method, TextWriter writer, string indentation)
        {
            // example: "    // static string SomeMethod" + <dumped parameters>
            writer.Write(
                "{0}// {1}{2} {3}",
                indentation, method.Static ? "static " : "", method.ReturnType.FullCSharpName, method.Name
            );
            DumpCSharpParameters(method.Parameters, writer);
        }
        
        public void DumpNativeHolder(ScriptGenerator.MethodInfo method, string uniqueMethodName, TextWriter writer, string indentation)
        {
            writer.Write(
                "{0}inline {1}",
                indentation, method.Static ? "Static" : "Instance"
            );
            if (method.ReturnType.IsVoid)
            {
                writer.Write("VoidMethod<");
            }
            else
            {
                writer.Write($"Method<{method.ReturnType.FullNativeName}");
                if (method.Parameters.Count > 0)
                    writer.Write(", ");
            }

            string parameters = string.Join(", ", method.Parameters.Select(it => it.Type.FullNativeName));
            writer.Write(
                "{0}> {1}(classLoader, \"{2} {3}",
                parameters, uniqueMethodName, method.ReturnType.FullCSharpName, method.Name
            );
            DumpCSharpParameters(method.Parameters, writer);
            writer.Write("\");");
        }

        void DumpCSharpParameters(IEnumerable<ScriptGenerator.ParameterInfo> parameters, TextWriter writer)
        {
            writer.Write(
                "({0})",
                string.Join(", ", parameters.Select(it => $"{it.Type.FullCSharpName} {it.Name}"))
            );
        }
    }
}