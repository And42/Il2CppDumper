#pragma once

#include <string>
#include <vector>
#include <stdexcept>

#include "methods.h"

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
                throw std::runtime_error("classTree must be not empty");
            } else if (classTree.size() == 1) {
                throw std::runtime_error("for 1 class in the tree use `Il2CppClassLoader` to avoid extra memory usage");
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
        bool hooked = false;

    public:
        inline StaticMethod(Il2CppClassLoader &classLoader, const std::string &methodSignature)
            : MethodBase(classLoader, methodSignature) {}

        inline void hook(Ret (*hookedMethod)(void *instance, Args... args)) {
        	if (hooked) {
                throw std::runtime_error("Can hook method only once");
        	}
            hookIl2CppMethod((void *) hookedMethod, (void **) &originalMethod);
            hooked = true;
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
        bool hooked = false;

    public:
        inline InstanceMethod(Il2CppClassLoader &classLoader, const std::string &methodSignature)
            : MethodBase(classLoader, methodSignature) {}

        inline void hook(Ret (*hookedMethod)(void *instance, Args... args)) {
        	if (hooked) {
                throw std::runtime_error("Can hook method only once");
        	}
            hookIl2CppMethod((void *) hookedMethod, (void **) &originalMethod);
            hooked = true;
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