INCLUDES = \
	-DG_LOG_DOMAIN=\"MonoDebuggerJitWrapper\" \
	@WRAPPER_CFLAGS@

lib_LIBRARIES = libmonodebuggerwrapper.a

libexec_PROGRAMS = mono-debugger-jit-wrapper

mono_debugger_jit_wrapper_SOURCES = \
	main.c

mono_debugger_jit_wrapper_LDADD = \
	libmonodebuggerwrapper.a	\
	@WRAPPER_LIBS@

libmonodebuggerwrapper_a_SOURCES = \
	wrapper.c			\
	thread-manager.c		\
	icall.c				\
	mono-debugger-jit-wrapper.h

