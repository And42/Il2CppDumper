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