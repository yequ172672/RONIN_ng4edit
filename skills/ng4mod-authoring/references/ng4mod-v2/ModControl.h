#pragma once

// Optional process-local control ABI. No Runtime import library is required.
// Callbacks own synchronization and persistence; they never expose game pointers.
#include <stdint.h>

#define NG4_MOD_CONTROL_VERSION 1u
#define NG4_MOD_CONTROL_EXPORT "Ng4ModQueryControl"
#define NG4_MOD_CALL __cdecl

enum Ng4ModControlResult
{
    NG4_MOD_OK = 0,
    NG4_MOD_INVALID = 1,
    NG4_MOD_UNAVAILABLE = 2,
    NG4_MOD_READ_ONLY = 3,
    NG4_MOD_IO_ERROR = 4,
    NG4_MOD_RESTART_REQUIRED = 5,
    NG4_MOD_PENDING = 6,
    NG4_MOD_INTERNAL_ERROR = 7
};
enum Ng4ModParameterKind
{
    NG4_MOD_BOOL = 0, NG4_MOD_INTEGER = 1, NG4_MOD_FLOAT = 2,
    NG4_MOD_ENUM = 3, NG4_MOD_KEY = 4, NG4_MOD_UINT64 = 5
};
enum Ng4ModParameterFlags
{
    NG4_MOD_PARAM_READ_ONLY = 1u,
    NG4_MOD_PARAM_RESTART = 2u,
    NG4_MOD_PARAM_NEXT_EVENT = 4u
};
enum Ng4ModFlags
{
    NG4_MOD_EXPERIMENTAL = 1u, NG4_MOD_STANDALONE = 2u
};
enum Ng4ModState
{
    NG4_MOD_NOT_INITIALIZED = 0, NG4_MOD_READY = 1, NG4_MOD_FAILED = 2
};

typedef struct Ng4ModInfo
{
    uint32_t struct_size;
    uint32_t state;
    uint32_t flags;
    uint32_t parameter_count;
    char id[96];
    char name[128];
    char version[32];
    char description[384];
} Ng4ModInfo;
typedef struct Ng4ModChoice
{
    double value;
    char label[64];
} Ng4ModChoice;
typedef struct Ng4ModParameter
{
    uint32_t struct_size;
    uint32_t kind;
    uint32_t flags;
    uint32_t choice_count;
    char id[96];
    char label[128];
    char description[256];
    double value;             // Value accepted by the Mod, including saved restart-only values.
    double minimum;
    double maximum;
    double step;
    Ng4ModChoice choices[24];
    uint64_t integer_value;   // Exact values for NG4_MOD_UINT64, never converted through double.
    uint64_t integer_minimum;
    uint64_t integer_maximum;
} Ng4ModParameter;
typedef struct Ng4ModApplyResult
{
    uint32_t struct_size;
    uint32_t code;
    double value;
    char message[256];
} Ng4ModApplyResult;
typedef struct Ng4ModControlApi
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t (NG4_MOD_CALL* get_info)(Ng4ModInfo*);
    uint32_t (NG4_MOD_CALL* get_parameter)(uint32_t index, Ng4ModParameter*);
    uint32_t (NG4_MOD_CALL* set_parameter)(const char* id, double value, Ng4ModApplyResult*);
    uint32_t (NG4_MOD_CALL* set_integer_parameter)(const char* id, uint64_t value, Ng4ModApplyResult*);
} Ng4ModControlApi;
typedef uint32_t (NG4_MOD_CALL* Ng4ModQueryControlFn)(uint32_t version, Ng4ModControlApi*);

#ifdef __cplusplus
#include <cmath>
#include <cstring>
#include <type_traits>

namespace ng4tool::mod_control
{
template<size_t N> inline void Text(char (&out)[N], const char* text) noexcept
{
    if (!text) text = "";
    const size_t length = std::strlen(text);
    const size_t count = length < N - 1 ? length : N - 1;
    std::memcpy(out, text, count);
    out[count] = '\0';
}
template<class T> inline bool Output(T* output) noexcept
{
    if (!output || output->struct_size != sizeof(T)) return false;
    *output = {};
    output->struct_size = sizeof(T);
    return true;
}
inline Ng4ModParameter Parameter(const char* id, const char* label, uint32_t kind,
    double value, double minimum = 0, double maximum = 1, double step = 1,
    uint32_t flags = 0, const char* description = "") noexcept
{
    Ng4ModParameter p{};
    p.struct_size = sizeof(p); p.kind = kind; p.flags = flags;
    Text(p.id, id); Text(p.label, label); Text(p.description, description);
    p.value = value; p.minimum = minimum; p.maximum = maximum; p.step = step;
    return p;
}
inline void Choice(Ng4ModParameter& p, double value, const char* label) noexcept
{
    if (p.choice_count >= 24) return;
    auto& choice = p.choices[p.choice_count++];
    choice.value = value; Text(choice.label, label);
}
inline Ng4ModParameter IntegerParameter(const char* id, const char* label, uint64_t value,
    uint64_t minimum, uint64_t maximum, uint32_t flags = 0, const char* description = "") noexcept
{
    auto p = Parameter(id, label, NG4_MOD_UINT64, 0, 0, 0, 0, flags, description);
    p.integer_value = value; p.integer_minimum = minimum; p.integer_maximum = maximum;
    return p;
}
inline bool ValidValue(const Ng4ModParameter& p, double value) noexcept
{
    if (!std::isfinite(value) || value < p.minimum || value > p.maximum) return false;
    if (p.kind != NG4_MOD_FLOAT && std::floor(value) != value) return false;
    if (p.kind == NG4_MOD_BOOL && value != 0 && value != 1) return false;
    if (p.kind == NG4_MOD_ENUM)
    {
        for (uint32_t i = 0; i < p.choice_count && i < 24; ++i)
            if (p.choices[i].value == value) return true;
        return false;
    }
    return true;
}
inline uint32_t Result(Ng4ModApplyResult* output, uint32_t code,
    double value, const char* message) noexcept
{
    if (!Output(output)) return NG4_MOD_INVALID;
    output->code = code; output->value = value; Text(output->message, message);
    return code;
}
inline uint32_t Api(uint32_t version, Ng4ModControlApi* output,
    uint32_t (NG4_MOD_CALL* info)(Ng4ModInfo*),
    uint32_t (NG4_MOD_CALL* parameter)(uint32_t, Ng4ModParameter*),
    uint32_t (NG4_MOD_CALL* set)(const char*, double, Ng4ModApplyResult*),
    uint32_t (NG4_MOD_CALL* setInteger)(const char*, uint64_t, Ng4ModApplyResult*) = nullptr) noexcept
{
    if (version != NG4_MOD_CONTROL_VERSION || !Output(output)) return NG4_MOD_INVALID;
    output->version = NG4_MOD_CONTROL_VERSION;
    output->get_info = info; output->get_parameter = parameter; output->set_parameter = set;
    output->set_integer_parameter = setInteger;
    return NG4_MOD_OK;
}
static_assert(sizeof(Ng4ModControlApi) == 40, "Mod control ABI requires Windows x64.");
static_assert(sizeof(Ng4ModInfo) == 656);
static_assert(sizeof(Ng4ModParameter) == 2280);
static_assert(sizeof(Ng4ModApplyResult) == 272);
static_assert(std::is_standard_layout_v<Ng4ModControlApi>);
static_assert(std::is_trivially_copyable_v<Ng4ModParameter>);
}
#endif
