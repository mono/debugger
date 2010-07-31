#include <libgtop-glue.h>
#include <unistd.h>
#include <dirent.h>
#include <fcntl.h>
#include <errno.h>
#include <string.h>
#include <stdio.h>

int
mono_debugger_libgtop_glue_get_pid (void)
{
	return getpid ();
}

/*
 * The following has been copied from libgtop 2.22.1
 */

static int
try_file_to_buffer (char *buffer, size_t bufsiz, const char *format, ...);

static inline int
proc_file_to_buffer (char *buffer, size_t bufsiz, const char *fmt, pid_t pid)
{
	return try_file_to_buffer(buffer, bufsiz, fmt, pid);
}

static inline int
proc_stat_to_buffer (char *buffer, size_t bufsiz, pid_t pid)
{
	return proc_file_to_buffer(buffer, bufsiz, "/proc/%d/stat", pid);
}

static inline int
proc_status_to_buffer (char *buffer, size_t bufsiz, pid_t pid)
{
	return proc_file_to_buffer(buffer, bufsiz, "/proc/%d/status", pid);
}

static inline int
proc_statm_to_buffer (char *buffer, size_t bufsiz, pid_t pid)
{
	return proc_file_to_buffer(buffer, bufsiz, "/proc/%d/statm", pid);
}

static inline char *
proc_stat_after_cmd (char *p)
{
	p = strrchr (p, ')');
	if (G_LIKELY(p))
		*p++ = '\0';
	return p;
}

static inline char*
next_token(const char *p)
{
	while (isspace(*p)) p++;
	return (char*) p;
}

static inline char *
skip_token (const char *p)
{
	p = next_token(p);
	while (*p && !isspace(*p)) p++;
	p = next_token(p);
	return (char *)p;
}

static inline char *
skip_multiple_token (const char *p, size_t count)
{
	while(count--)
		p = skip_token (p);

	return (char *)p;
}

enum TRY_FILE_TO_BUFFER
{
	TRY_FILE_TO_BUFFER_OK = 0,
	TRY_FILE_TO_BUFFER_OPEN = -1,
	TRY_FILE_TO_BUFFER_READ = -2
};

static int try_file_to_buffer(char *buffer, size_t bufsiz, const char *format, ...)
{
	char path[4096];
	int fd;
	ssize_t len;
	va_list pa;

	if (bufsiz <= sizeof(char*))
	  g_warning("Huhu, bufsiz of %lu looks bad", (gulong)bufsiz);

	va_start(pa, format);

	/* C99 also provides vsnprintf */
	g_vsnprintf(path, sizeof path, format, pa);

	va_end(pa);

	buffer [0] = '\0';

	if((fd = open (path, O_RDONLY)) < 0)
		return TRY_FILE_TO_BUFFER_OPEN;

	len = read (fd, buffer, bufsiz - 1);
	close (fd);

	if (len < 0)
		return TRY_FILE_TO_BUFFER_READ;

	buffer [len] = '\0';

	return TRY_FILE_TO_BUFFER_OK;
}

/*
 * The following has been copied from libgtop 2.22.1 and locally modified.
 */

gboolean
mono_debugger_libgtop_glue_get_memory (int pid, LibGTopGlueMemoryInfo *info)
{
	char buffer [BUFSIZ], *p;

	if (proc_stat_to_buffer (buffer, sizeof buffer, pid))
		return FALSE;

	p = proc_stat_after_cmd (buffer);
	if (!p) return FALSE;

	p = skip_multiple_token (p, 20);

	info->vsize    = strtoull (p, &p, 0);
	info->rss      = strtoull (p, &p, 0);
	info->rss_rlim = strtoull (p, &p, 0);

	if (proc_statm_to_buffer (buffer, sizeof buffer, pid))
		return FALSE;

	info->size     = strtoull (buffer, &p, 0);
	info->resident = strtoull (p, &p, 0);
	info->share    = strtoull (p, &p, 0);

	return TRUE;
}

gboolean
mono_debugger_libgtop_glue_get_open_files (int pid, int *result)
{
	int count = 0;
	char fn [BUFSIZ];
	struct dirent *direntry;
	DIR *dir;

	sprintf (fn, "/proc/%d/fd", pid);

	dir = opendir (fn);
	if (!dir) return FALSE;

	while((direntry = readdir(dir))) {
		if(direntry->d_name[0] == '.')
			continue;

		count++;
	}

	closedir (dir);
	*result = count;
	return TRUE;
}
