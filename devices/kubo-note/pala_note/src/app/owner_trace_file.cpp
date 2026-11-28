#include "owner_trace.h"
#if PALA_VFS_OWNER_TRACE
#include "owner_trace_hooks.h"
#include <dirent.h>
#include <stdio.h>
#include <sys/types.h>

struct _reent;
struct stat;
extern "C" {

// Native libc calls the underscored syscall entry points. In this SDK the
// corresponding esp_vfs_* names are aliases for those same implementations.
// Track argument-free context only; paths, streams and data are not retained.
#define OBSERVE_FILE(result, symbol, operation, signature, arguments)                     \
  result __real_##symbol signature noexcept;                                              \
  result __wrap_##symbol signature {                                                      \
    owner_trace::Frame frame(owner_trace::Layer::Vfs,                                     \
        static_cast<uint32_t>(owner_trace::FatOp::operation),                             \
        reinterpret_cast<uintptr_t>(__builtin_return_address(0)));                       \
    return __real_##symbol arguments;                                                     \
  }

OBSERVE_FILE(int, _open_r, VfsOpen, (_reent* r, const char* path, int flags, int mode), (r,path,flags,mode))
OBSERVE_FILE(ssize_t, _read_r, VfsRead, (_reent* r, int fd, void* buf, size_t n), (r,fd,buf,n))
OBSERVE_FILE(ssize_t, _write_r, VfsWrite, (_reent* r, int fd, const void* buf, size_t n), (r,fd,buf,n))
OBSERVE_FILE(int, _close_r, VfsClose, (_reent* r, int fd), (r,fd))
OBSERVE_FILE(int, _stat_r, VfsStat, (_reent* r, const char* path, struct stat* st), (r,path,st))
OBSERVE_FILE(int, _fstat_r, VfsFstat, (_reent* r, int fd, struct stat* st), (r,fd,st))
OBSERVE_FILE(DIR*, opendir, VfsOpenDir, (const char* path), (path))
OBSERVE_FILE(struct dirent*, readdir, VfsReadDir, (DIR* dir), (dir))
OBSERVE_FILE(int, closedir, VfsCloseDir, (DIR* dir), (dir))
OBSERVE_FILE(FILE*, fdopen, Fdopen, (int fd, const char* mode), (fd,mode))
OBSERVE_FILE(int, setvbuf, Setvbuf, (FILE* stream, char* buf, int mode, size_t n), (stream,buf,mode,n))
OBSERVE_FILE(size_t, fwrite, Fwrite, (const void* data, size_t size, size_t n, FILE* stream), (data,size,n,stream))
#undef OBSERVE_FILE
}
#endif
