libexec_PROGRAMS = mono-debugger-mini-wrapper

INCLUDES = \
	-DG_LOG_DOMAIN=\"MonoDebuggerJitWrapper\" \
	@WRAPPER_CFLAGS@

mono_debugger_mini_wrapper_SOURCES = \
	mini-main.c			\
	wrapper.c			\
	thread-manager.c		\
	icall.c				\
	mono-debugger-jit-wrapper.h

mono_debugger_mini_wrapper_LDADD = \
	@WRAPPER_LIBS@



