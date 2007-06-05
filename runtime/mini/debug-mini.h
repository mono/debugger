#ifndef __DEBUG_MINI_H__
#define __DEBUG_MINI_H__

#include <mono/metadata/class-internals.h>
#include <mono/metadata/mono-debug-debugger.h>

void            mono_debugger_insert_method_breakpoint    (MonoMethod *method, guint64 idx);
int             mono_debugger_remove_method_breakpoint    (guint64 index);

#endif
