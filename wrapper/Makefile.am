INCLUDES = \
	-DG_LOG_DOMAIN=\"MonoDebuggerJitWrapper\" \
	@WRAPPER_CFLAGS@

libexec_PROGRAMS = mono-debugger-jit-wrapper

mono_debugger_jit_wrapper_SOURCES = \
	main.c				\
	thread-manager.c		\
	icall.c				\
	mono-debugger-jit-wrapper.h

mono_debugger_jit_wrapper_LDADD = \
	@WRAPPER_LIBS@


