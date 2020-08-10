using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2CppDumper
{
    public class TlApiDumper
    {
        #region MethodsContents
        private static readonly string MethodsContents = TrimMargin(@"
            #pragma once

            #include <string>

            namespace mods_api::methods {

                struct M_Il2CppClass;
                struct M_Il2CppStaticFieldInfo;

                M_Il2CppClass *getIl2CppClass(
                    const std::string &classNamespace,
                    const std::string &className
                );

                M_Il2CppClass *getIl2CppNestedClass(
                    M_Il2CppClass *containerClass,
                    const std::string &nestedClassName
                );

                int32_t getIl2CppInstanceFieldOffset(
                    M_Il2CppClass *il2CppClass,
                    const std::string &fieldName
                );

                M_Il2CppStaticFieldInfo *getIl2CppStaticFieldInfo(
                    M_Il2CppClass *il2CppClass,
                    const std::string &fieldName
                );

                void setIl2CppStaticFieldValue(
                    M_Il2CppStaticFieldInfo *staticFieldInfo,
                    void *value
                );

                void getIl2CppStaticFieldValue(
                    M_Il2CppStaticFieldInfo *staticFieldInfo,
                    void *value
                );

                void getIl2CppMethod(
                    M_Il2CppClass *il2CppClass,
                    const std::string &methodSignature,
                    void **originalMethod
                );

                void hookIl2CppMethod(
                    M_Il2CppClass *il2CppClass,
                    const std::string &methodSignature,
                    void *newMethod,
                    void **originalMethod
                );
            }
        ");
        #endregion

        #region ClassesContents
        private static readonly string ClassesContents = TrimMargin(@"
            #pragma once

            #include <string>
            #include <vector>
            #include <stdexcept>

            #include ""methods.h""

            namespace mods_api::classes {

                class Il2CppClassLoader {
                private:
                    const std::string className;
                    methods::M_Il2CppClass *il2CppClass = nullptr;

                protected:
                    const std::string classNamespace;

                    inline virtual methods::M_Il2CppClass *getClassInternal() {
                        return methods::getIl2CppClass(classNamespace, className);
                    }

                public:
                    inline Il2CppClassLoader(const std::string &classNamespace, const std::string &className)
                        : classNamespace(classNamespace), className(className) {}

                    inline methods::M_Il2CppClass *getClass() {
                        if (il2CppClass == nullptr) {
                            il2CppClass = getClassInternal();
                        }
                        return il2CppClass;
                    }
                };

                class Il2CppNestedClassLoader : public Il2CppClassLoader {
                private:
                    std::vector<std::string> classTree;

                protected:
                    inline methods::M_Il2CppClass *getClassInternal() override {
                        methods::M_Il2CppClass *il2CppClass = nullptr;
                        for (size_t treeDepth = 0; treeDepth < classTree.size(); ++treeDepth) {
                            std::string &currentClass = classTree[treeDepth];
                            if (treeDepth == 0) {
                                il2CppClass = methods::getIl2CppClass(classNamespace, currentClass);
                            } else {
                                il2CppClass = methods::getIl2CppNestedClass(il2CppClass, currentClass);
                            }
                        }
                        return il2CppClass;
                    }

                public:
                    inline Il2CppNestedClassLoader(
                        const std::string &classNamespace,
                        const std::initializer_list<std::string> &classTree
                    ) : Il2CppClassLoader(classNamespace, nullptr) {
                        if (classTree.size() == 0) {
                            throw std::runtime_error(""classTree must be not empty"");
                        } else if (classTree.size() == 1) {
                            throw std::runtime_error(""for 1 class in the tree use `Il2CppClassLoader` to avoid extra memory usage"");
                        }

                        this->classTree.reserve(classTree.size());
                        for (const std::string &clazz : classTree) {
                            this->classTree.emplace_back(clazz);
                        }
                    }
                };

                class MethodBase {
                protected:
                    Il2CppClassLoader &classLoader;
                    std::string methodSignature;

                    inline MethodBase(Il2CppClassLoader &classLoader, const std::string &methodSignature)
                        : classLoader(classLoader), methodSignature(methodSignature) {}

                    inline void getIl2CppMethod(void **originalMethod) {
                        methods::getIl2CppMethod(classLoader.getClass(), methodSignature, originalMethod);
                    }

                    inline void hookIl2CppMethod(void *newMethod, void **originalMethod) {
                        methods::hookIl2CppMethod(
                            classLoader.getClass(), methodSignature, newMethod, originalMethod
                        );
                    }
                };

                template<typename Ret, typename... Args>
                class StaticMethod : MethodBase {
                private:
                    Ret (*originalMethod)(Args... args) = nullptr;

                public:
                    inline StaticMethod(Il2CppClassLoader &classLoader, const std::string &methodSignature)
                        : MethodBase(classLoader, methodSignature) {}

                    inline void hook(
                        Ret (*hookedMethod)(void *instance, Args... args),
                        Ret (*&originalMethodForHooked)(void *instance, Args... args)
                    ) {
                        hookIl2CppMethod((void *) hookedMethod, (void **) &originalMethodForHooked);
                    }

                    inline Ret call(Args... args) {
                        if (originalMethod == nullptr) {
                            getIl2CppMethod((void **) &originalMethod);
                        }
                        return originalMethod(args...);
                    }
                };

                template<typename Ret, typename... Args>
                class InstanceMethod : MethodBase {
                private:
                    Ret (*originalMethod)(void *instance, Args... args) = nullptr;

                public:
                    inline InstanceMethod(Il2CppClassLoader &classLoader, const std::string &methodSignature)
                        : MethodBase(classLoader, methodSignature) {}

                    inline void hook(
                        Ret (*hookedMethod)(void *instance, Args... args),
                        Ret (*&originalMethodForHooked)(void *instance, Args... args)
                    ) {
                        hookIl2CppMethod((void *) hookedMethod, (void **) &originalMethodForHooked);
                    }

                    inline Ret call(void *instance, Args... args) {
                        if (originalMethod == nullptr) {
                            getIl2CppMethod((void **) &originalMethod);
                        }
                        return originalMethod(instance, args...);
                    }
                };

                class FieldBase {
                protected:
                    Il2CppClassLoader &classLoader;
                    std::string fieldName;

                    inline FieldBase(Il2CppClassLoader &classLoader, const std::string &fieldName)
                        : classLoader(classLoader), fieldName(fieldName) {}
                };

                template<typename VALUE>
                class ConstantField : FieldBase {
                private:
                    methods::M_Il2CppStaticFieldInfo *staticFieldInfo = nullptr;

                    inline methods::M_Il2CppStaticFieldInfo *getFieldInfo() {
                        if (staticFieldInfo == nullptr) {
                            staticFieldInfo = methods::getIl2CppStaticFieldInfo(classLoader.getClass(), fieldName);
                        }
                        return staticFieldInfo;
                    }

                public:
                    inline ConstantField(Il2CppClassLoader &classLoader, const std::string &fieldName)
                        : FieldBase(classLoader, fieldName) {}

                    inline VALUE get() {
                        VALUE value = VALUE();
                        methods::getIl2CppStaticFieldValue(getFieldInfo(), &value);
                        return value;
                    }
                };

                template<typename VALUE>
                class StaticField : FieldBase {
                private:
                    methods::M_Il2CppStaticFieldInfo *staticFieldInfo = nullptr;

                    inline methods::M_Il2CppStaticFieldInfo *getFieldInfo() {
                        if (staticFieldInfo == nullptr) {
                            staticFieldInfo = methods::getIl2CppStaticFieldInfo(classLoader.getClass(), fieldName);
                        }
                        return staticFieldInfo;
                    }

                public:
                    inline StaticField(Il2CppClassLoader &classLoader, const std::string &fieldName)
                        : FieldBase(classLoader, fieldName) {}

                    inline VALUE get() {
                        VALUE value = VALUE();
                        methods::getIl2CppStaticFieldValue(getFieldInfo(), &value);
                        return value;
                    }

                    inline void set(VALUE value) {
                        methods::setIl2CppStaticFieldValue(getFieldInfo(), &value);
                    }
                };

                template<typename VALUE>
                class InstanceField : FieldBase {
                private:
                    int32_t fieldOffset = -1;

                    inline int32_t getFieldOffset() {
                        if (fieldOffset == -1) {
                            fieldOffset = methods::getIl2CppInstanceFieldOffset(classLoader.getClass(), fieldName);
                        }
                        return fieldOffset;
                    }

                public:
                    inline InstanceField(Il2CppClassLoader &classLoader, const std::string &fieldName)
                        : FieldBase(classLoader, fieldName) {}

                    inline VALUE get(void *instance) {
                        return *(VALUE *) ((char *) instance + getFieldOffset());
                    }

                    inline void set(void *instance, VALUE value) {
                        *(VALUE *) ((char *) instance + getFieldOffset()) = value;
                    }
                };
            }
        ");
        #endregion
        
        #region CommonTypesContents
        private static readonly string CommonTypesContents = TrimMargin(@"
            #pragma once            

            #include ""il2cpp.h""

            namespace mods_api::common_unity_types {

                // primitives
                using un_bool   = int8_t;
                using un_char   = uint16_t;
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

                // types 
                using un_string = System_String_o;
            }
        ");
        #endregion

        private static readonly string TypesIncludeContents = TrimMargin(@"
            #pragma once

            #include ""il2cpp.h""
            #include ""common_unity_types.h""
            #include ""classes.h""
        ");

        private static readonly string ClassesInnerNamespaceHeader = TrimMargin(@"
            using namespace mods_api::classes;
            using namespace mods_api::common_unity_types;
        ", indentation: "    ");

        private static string TrimMargin(
            string input,
            string indentation = "",
            bool removeStartBlankLines = true,
            bool removeEndBlankLines = true
        )
        {
            var split = input.Split(new[] {Environment.NewLine}, StringSplitOptions.None);
            IEnumerable<string> lines = split;
            int minimalNonEmptyIndent = int.MaxValue;
      
            int firstNonBlankLineIndex = -1;
            int lastNonBlankLineIndex = -1;
            for (int i = 0; i < split.Length; i++)
            {
                string currentLine = split[i];
                int firstNonSpaceCharIndex = -1;
                for (int charIndex = 0; charIndex < currentLine.Length; charIndex++)
                {
                    if (!char.IsWhiteSpace(currentLine[charIndex]))
                    {
                        firstNonSpaceCharIndex = charIndex;
                        break;
                    }
                }
                if (firstNonSpaceCharIndex == -1)
                    continue;

                if (firstNonSpaceCharIndex < minimalNonEmptyIndent)
                    minimalNonEmptyIndent = firstNonSpaceCharIndex;

                if (firstNonBlankLineIndex == -1)
                    firstNonBlankLineIndex = i;
                lastNonBlankLineIndex = i;
            }

            if (removeStartBlankLines && firstNonBlankLineIndex != -1)
                lines = lines.Skip(firstNonBlankLineIndex);
            if (removeEndBlankLines && lastNonBlankLineIndex != -1)
                lines = lines.Take(lastNonBlankLineIndex - firstNonBlankLineIndex + 1);
            
            return string.Join(
                Environment.NewLine,
                string.IsNullOrEmpty(indentation)
                    ? minimalNonEmptyIndent == int.MaxValue
                        ? lines
                        : lines.Select(it => it.Substring(Math.Min(minimalNonEmptyIndent, it.Length)))
                    : minimalNonEmptyIndent == int.MaxValue
                        ? lines.Select(it => indentation + it)
                        : lines.Select(it => indentation + it.Substring(Math.Min(minimalNonEmptyIndent, it.Length)))
            );
        }

        public void Dump(IEnumerable<ScriptGenerator.ImageInfo> images, string initialDirectory)
        {
            if (Directory.Exists(initialDirectory))
                Directory.Delete(initialDirectory, true);
            Directory.CreateDirectory(initialDirectory);
            
            File.WriteAllText(
                Path.Combine(initialDirectory, "common_unity_types.h"),
                CommonTypesContents, Encoding.UTF8
            );
            
            File.WriteAllText(
                Path.Combine(initialDirectory, "types_include.h"),
                TypesIncludeContents, Encoding.UTF8
            );
            
            File.WriteAllText(
                Path.Combine(initialDirectory, "methods.h"),
                MethodsContents, Encoding.UTF8
            );
            
            File.WriteAllText(
                Path.Combine(initialDirectory, "classes.h"),
                ClassesContents, Encoding.UTF8
            );

            string typesDir = Path.Combine(initialDirectory, "types");
            Directory.CreateDirectory(typesDir);
            foreach (var image in images)
                Dump(image, typesDir);
        }

        private void Dump(ScriptGenerator.ImageInfo image, string initialDirectory)
        {
            foreach (ScriptGenerator.TypeInfo type in image.Types)
                Dump(image, type, initialDirectory);
        }

        private void Dump(
            ScriptGenerator.ImageInfo image,
            ScriptGenerator.TypeInfo type,
            string initialDirectory
        )
        {
            var classTree = new List<ScriptGenerator.TypeInfo>();
            if (type.DeclaringClass == null)
            {
                classTree.Add(type);
            }
            else
            {
                var currentType = type;
                while (currentType != null)
                {
                    classTree.Add(currentType);
                    currentType = currentType.DeclaringClass;
                }
                classTree.Reverse();
            }
            
            var parts = new List<string>();
            parts.Add(ScriptGenerator.FixName(image.Name));
            parts.AddRange(
                classTree[0].Namespace
                    .Split('.')
                    .Select(ScriptGenerator.FixName)
                    .Select(it => it == string.Empty ? "_" : it)
            );
            parts.AddRange(classTree.Select(it => ScriptGenerator.FixName(it.Name)));

            string resultFile = Path.Combine(initialDirectory, Path.Combine(parts.ToArray())) + ".h";
            string resultFileDirectory = Path.GetDirectoryName(resultFile);
            if (resultFileDirectory != null && !Directory.Exists(resultFileDirectory))
                Directory.CreateDirectory(resultFileDirectory);

            using (var writer = new StreamWriter(resultFile, false, Encoding.UTF8))
            {
                writer.WriteLine("#pragma once");
                writer.WriteLine();
                writer.Write("#include \"");
                for (int i = 0; i < parts.Count; i++)
                    writer.Write("../");
                writer.WriteLine("types_include.h\"");
                writer.WriteLine();
                const string innerIndentation = "    ";
                // namespace is set only in the top level class; so, use it
                string @namespace = string.Join("::", parts);
                writer.WriteLine("namespace mods_api::types::{0} {{", @namespace);
                writer.WriteLine();
                writer.WriteLine(ClassesInnerNamespaceHeader);
                writer.WriteLine();
                writer.Write("{0}inline", innerIndentation);
                if (classTree.Count == 1)
                {
                    writer.WriteLine(" Il2CppClassLoader classLoader(\"{0}\", \"{1}\");", type.Namespace, type.Name);
                } else
                {
                    var treeString = string.Join(", ", classTree.Select(it => $"\"{it.Name}\""));
                    writer.WriteLine(
                        " Il2CppNestedClassLoader classLoader(\"{0}\", {{{1}}});", classTree[0].Namespace, treeString
                    );
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

                void DumpUniqueItems<T>(Action<T, string, TextWriter, string> dumper, IDictionary<T, string> unique,
                    ICollection<T> items, bool addSpacer)
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

                writer.Write('}');
            }
        }

        private void Dump(ScriptGenerator.FieldInfo field, TextWriter writer, string indentation)
        {
            // example: "    // static string SomeField"
            writer.WriteLine(
                "{0}// {1}{2} {3}",
                indentation, field.Static ? "static " : "", field.Type.FullCSharpName, field.Name
            );
            
            // example: "    inline StaticField<string> SomeField(classLoader, "SomeField");"
            writer.Write(
                "{0}inline {1}Field<{2}> {3}(classLoader, \"{4}\");",
                indentation, field.Static ? "Static" : "Instance", field.Type.FullNativeName, ScriptGenerator.FixName(field.Name), field.Name
            );
        }

        private void Dump(ScriptGenerator.MethodInfo method, string uniqueMethodName, TextWriter writer, string indentation)
        {
            DumpCSharpComment(method, writer, indentation);
            writer.WriteLine();
            DumpNativeHolder(method, uniqueMethodName, writer, indentation);
        }

        private void DumpCSharpComment(ScriptGenerator.MethodInfo method, TextWriter writer, string indentation)
        {
            // example: "    // static string SomeMethod" + <dumped parameters>
            writer.Write(
                "{0}// {1}{2} {3}",
                indentation, method.Static ? "static " : "", method.ReturnType.FullCSharpName, method.Name
            );
            DumpCSharpParameters(method.Parameters, writer);
        }

        private void DumpNativeHolder(ScriptGenerator.MethodInfo method, string uniqueMethodName, TextWriter writer, string indentation)
        {
            writer.Write(
                "{0}inline {1}Method<{2}",
                indentation, method.Static ? "Static" : "Instance", method.ReturnType.IsVoid ? "void" : method.ReturnType.FullNativeName
            );
            if (method.Parameters.Count > 0)
                writer.Write(", ");

            string parameters = string.Join(", ", method.Parameters.Select(it => it.Type.FullNativeName));
            writer.Write(
                "{0}> {1}(classLoader, \"{2} {3}",
                parameters, uniqueMethodName, method.ReturnType.FullCSharpName, method.Name
            );
            DumpCSharpParameters(method.Parameters, writer);
            writer.Write("\");");
        }

        private void DumpCSharpParameters(IEnumerable<ScriptGenerator.ParameterInfo> parameters, TextWriter writer)
        {
            writer.Write(
                "({0})",
                string.Join(", ", parameters.Select(it => $"{it.Type.FullCSharpName} {it.Name}"))
            );
        }
    }
}