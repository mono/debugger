AC_DEFUN([READLINE_TRYLINK], [
    lib="$1"

    old_LIBS=$LIBS
    LIBS="-l$lib"

    AC_TRY_LINK(,,[READLINE_DEPLIBS=$LIBS],[
		LIBS="-l$lib -ltermcap"
		AC_TRY_LINK(,,[
			READLINE_DEPLIBS=$LIBS
		],[
			LIBS="-l$1 -lcurses"
			AC_TRY_LINK(,,[
				READLINE_DEPLIBS=$LIBS
			],[
				LIBS="-l$1 -lncurses"
				AC_TRY_LINK(,,[
					READLINE_DEPLIBS=$LIBS
				],[
					READLINE_DEPLIBS=
				])
			])
		])
    ])

    LIBS=$old_LIBS
])

AC_DEFUN([CHECK_READLINE],  [
        AC_ARG_WITH(readline,   [  -with-readline=[no/yes/libedit]    Enable readline support (default=yes)])
	AC_CACHE_CHECK([for Readline], ac_cv_with_readline, ac_cv_with_readline="${with_readline:=yes}")
	case $ac_cv_with_readline in
	no|"")
		with_readline=no
		;;
	yes)
		with_readline=yes
		;;
	libedit)
		with_readline=libedit;
		;;
	esac

	if test "$with_readline" != no; then
	   READLINE_DEPLIBS=
	   if test "$with_readline" == yes; then
	      READLINE_TRYLINK(readline)
	   fi

	   # fall through to checking for libedit if we didn't find
	   # libreadline (or if you user specified libedit)
	   if test -z "$READLINE_DEPLIBS"; then
	      READLINE_TRYLINK(edit)

	      AC_DEFINE(READLINE_IS_LIBEDIT,1,[if we're using the readline api from libedit])
	   fi

	   if test -z "$READLINE_DEPLIBS"; then
	      AC_MSG_ERROR([Cannot figure out how to link with the readline/libedit library; see config.log for more information])
	   fi
	fi
])
