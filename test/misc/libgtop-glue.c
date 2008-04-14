#include <libgtop-glue.h>
#include <sys/wait.h>
#include <unistd.h>
#include <fcntl.h>

int
mono_debugger_libgtop_glue_get_pid (void)
{
	return getpid ();
}

gboolean
mono_debugger_libgtop_glue_get_memory (glibtop *server, int pid, LibGTopGlueMemoryInfo *info)
{
	glibtop_proc_mem proc_mem;

	memset (&proc_mem, 0, sizeof (glibtop_proc_mem));

	glibtop_get_proc_mem (&proc_mem, pid);
	if (proc_mem.flags == 0)
		return FALSE;

	info->pagesize = getpagesize ();
	info->size = proc_mem.size;
	info->vsize = proc_mem.vsize;
	info->resident = proc_mem.resident;
	info->share = proc_mem.share;
	info->rss = proc_mem.rss;
	info->rss_rlim = proc_mem.rss_rlim;

	return TRUE;
}

gboolean
mono_debugger_libgtop_glue_get_open_files (glibtop *server, int pid, int *result)
{
	glibtop_proc_open_files proc_files;

	memset (&proc_files, 0, sizeof (glibtop_proc_open_files));
	glibtop_get_proc_open_files (&proc_files, pid);
	if (proc_files.flags == 0)
		return FALSE;

	*result = (int) proc_files.number;
	return TRUE;
}

gboolean
mono_debugger_libgtop_glue_test (void)
{
	pid_t pid;
	int ret, status;

	pid = fork ();
	if (pid == 0) {
		int fd = open ("/dev/null", O_RDWR);
		dup2 (fd, 1);
		dup2 (fd, 2);
		execl ("/bin/date", "date", NULL);
	} else if (pid < 0) {
		g_error (G_STRLOC ": fork() failed: %s", g_strerror (errno));
	}

	ret = waitpid (pid, &status, 0);
	if (ret < 0)
		g_error (G_STRLOC ": waitpid(%d) failed: %s", pid, g_strerror (errno));
	else if (ret != pid)
		g_error (G_STRLOC ": waitpid(%d) returned %d", pid, ret);

	return TRUE;
}
