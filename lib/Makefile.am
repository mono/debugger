bin_SCRIPTS = Interpreter.exe @GUI_TARGETS@ mdb mono-debug 

assdir = $(libdir)

ass_SCRIPTS = Mono.Debugger.dll Mono.Debugger.Backend.dll 

noinst_SCRIPTS = SymbolFileReader.exe

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
	@top_srcdir@/backends/native/*.cs		\
	@top_srcdir@/arch/*.cs

SYMFILE_READER_SRCLIST = \
	@top_srcdir@/lib/SymbolFileReader.cs

LIBRARY_DEPS = \
	-r Mono.CSharp.Debugger

BACKEND_DEPS = \
	-r Mono.GetOptions	\
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
	-r Mono.GetOptions	\
	-r Mono.CSharp.Debugger	\
	-r Mono.Debugger.dll	\
	-r Mono.Debugger.Backend.dll			\
	-r ../widgets/gtk/gtksourceview-sharp.dll	\
	-r ../widgets/mono.debugger.gui/debugger-widgets.dll

INTERPRETER_DEPS = \
	-r Mono.GetOptions	\
	-r Mono.CSharp.Debugger	\
	-r Mono.Debugger.dll	\
	-r Mono.Debugger.Backend.dll

SYMFILE_READER_DEPS = \
	-r Mono.GetOptions	\
	-r Mono.CSharp.Debugger

GUIDIR = @top_srcdir@/frontends/gui
RESOURCES = \
	-resource:$(GUIDIR)/debugger.glade,debugger.glade	\
	-resource:$(GUIDIR)/step-into.png,step-into.png		\
	-resource:$(GUIDIR)/step-over.png,step-over.png		\
	-resource:$(GUIDIR)/mono.png,mono.png			\
	-resource:$(GUIDIR)/stop.png,stop.png			\
	-resource:$(GUIDIR)/line.png,line.png

Mono.Debugger.dll: $(LIBRARY_SRCLIST)
	$(MCS) /target:library /out:Mono.Debugger.dll $(MCS_FLAGS) $(LIBRARY_DEPS) $(LIBRARY_SRCLIST)

Mono.Debugger.Backend.dll: $(BACKEND_SRCLIST) Mono.Debugger.dll
	$(MCS) /target:library /out:Mono.Debugger.Backend.dll $(MCS_FLAGS) $(BACKEND_DEPS) $(BACKEND_SRCLIST)

Debugger.exe: $(ALL_SRCLIST) Mono.Debugger.dll Mono.Debugger.Backend.dll $(GUIDIR)/debugger.glade
	$(MCS) -o Debugger.exe $(DEPS) $(ALL_SRCLIST) $(RESOURCES) $(MCS_FLAGS)

Interpreter.exe: $(INTERPRETER_SRCLIST) Mono.Debugger.dll Mono.Debugger.Backend.dll
	$(MCS) -o Interpreter.exe $(INTERPRETER_DEPS) $(INTERPRETER_SRCLIST) $(MCS_FLAGS)

SymbolFileReader.exe: $(SYMFILE_READER_SRCLIST)
	$(MCS) -o SymbolFileReader.exe $(SYMFILE_READER_DEPS) $(SYMFILE_READER_SRCLIST) $(MCS_FLAGS)

Debugger.exe.config: Debugger.exe.config.in Makefile
	sed -e 's,\@MONO\@,${libexecdir}/mono-debugger-mini-wrapper,g' \
		-e 's,\@PATH\@,${PATH},g' \
		-e 's,\@LD_LIBRARY_PATH\@,${libdir}:${LD_LIBRARY_PATH},g' \
		< $< > tmp-t && mv tmp-t $@

Interpreter.exe.config: Interpreter.exe.config.in Makefile
	sed -e 's,\@MONO\@,${libexecdir}/mono-debugger-mini-wrapper,g' \
		-e 's,\@PATH\@,${PATH},g' \
		-e 's,\@LD_LIBRARY_PATH\@,${libdir}:${LD_LIBRARY_PATH},g' \
		< $< > tmp-t && mv tmp-t $@

configdir = $(bindir)
config_DATA = Debugger.exe.config Interpreter.exe.config

CLEANFILES = *.exe *.dll

EXTRA_DIST = \
	mdb.in mono-debug.in SymbolFileReader.cs \
	Debugger.exe.config.in Interpreter.exe.config.in

mdb: mdb.in
	sed -e 's^\@bindir\@^$(bindir)^g' < $(srcdir)/mdb.in > mdb.tmp \
	&& mv mdb.tmp mdb 

mono-debug: mono-debug.in
	sed -e 's^\@bindir\@^$(bindir)^g' < $(srcdir)/mono-debug.in > mono-debug.tmp \
	&& mv mono-debug.tmp mono-debug 
