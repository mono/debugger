noinst_SCRIPTS = Mono.Debugger.dll Mono.Debugger.Backend.dll @GUI_TARGETS@ Interpreter.exe

EXTRA_SCRIPTS = Debugger.exe

ALL_SRCLIST = \
	@top_srcdir@/frontends/gui/*.cs			\
	@top_srcdir@/frontends/scripting/*.cs		\
	@top_srcdir@/frontends/command-line/Interpreter.cs

INTERPRETER_SRCLIST = \
	@top_srcdir@/frontends/command-line/*.cs	\
	@top_srcdir@/frontends/scripting/*.cs

LIBRARY_SRCLIST = \
	@top_srcdir@/interfaces/*.cs			\
	@top_srcdir@/classes/*.cs

BACKEND_SRCLIST = \
	@top_srcdir@/backends/*.cs			\
	@top_srcdir@/backends/classes/*.cs		\
	@top_srcdir@/backends/ptrace/*.cs		\
	@top_srcdir@/backends/mono-csharp/*.cs		\
	@top_srcdir@/arch/*.cs

LIBRARY_DEPS = \
	-r Mono.CSharp.Debugger

BACKEND_DEPS = \
	-r Mono.Debugger.dll	\
	-r Mono.CSharp.Debugger

DEPS = \
	-r glib-sharp.dll	\
	-r pango-sharp.dll	\
	-r atk-sharp.dll	\
	-r gdk-sharp.dll	\
	-r gtk-sharp.dll	\
	-r gnome-sharp.dll	\
	-r glade-sharp.dll	\
	-r System.Drawing	\
	-r Mono.CSharp.Debugger	\
	-r Mono.Debugger.dll	\
	-r Mono.Debugger.Backend.dll			\
	-r ../widgets/gtk/gtksourceview-sharp.dll	\
	-r ../widgets/mono.debugger.gui/debugger-widgets.dll

INTERPRETER_DEPS = \
	-r Mono.CSharp.Debugger	\
	-r Mono.Debugger.dll	\
	-r Mono.Debugger.Backend.dll

GUIDIR = @top_srcdir@/frontends/gui
RESOURCES = \
	-resource:$(GUIDIR)/debugger.glade,debugger.glade	\
	-resource:$(GUIDIR)/mono.png,mono.png			\
	-resource:$(GUIDIR)/stop.png,stop.png			\
	-resource:$(GUIDIR)/line.png,line.png

Mono.Debugger.dll: $(LIBRARY_SRCLIST)
	$(MCS) /target:library /out:Mono.Debugger.dll $(MCS_FLAGS) $(LIBRARY_DEPS) $(LIBRARY_SRCLIST)

Mono.Debugger.Backend.dll: $(BACKEND_SRCLIST) Mono.Debugger.dll
	$(MCS) /target:library /out:Mono.Debugger.Backend.dll $(MCS_FLAGS) $(BACKEND_DEPS) $(BACKEND_SRCLIST)

Debugger.exe: $(ALL_SRCLIST) Mono.Debugger.dll Mono.Debugger.Backend.dll
	$(MCS) -o Debugger.exe $(DEPS) $(ALL_SRCLIST) $(RESOURCES) $(MCS_FLAGS)

Interpreter.exe: $(INTERPRETER_SRCLIST) Mono.Debugger.dll Mono.Debugger.Backend.dll
	$(MCS) -o Interpreter.exe $(INTERPRETER_DEPS) $(INTERPRETER_SRCLIST) $(MCS_FLAGS)

CLEANFILES = *.exe *.dll

