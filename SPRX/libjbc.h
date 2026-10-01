#pragma once

typedef uint64_t size_t;
typedef int64_t ssize_t;
typedef int64_t off_t;
typedef uint32_t uid_t;
typedef uint32_t gid_t;

struct jbc_cred
{
    uid_t uid;
    uid_t ruid;
    uid_t svuid;
    gid_t rgid;
    gid_t svgid;
    uintptr_t prison;
    uintptr_t cdir;
    uintptr_t rdir;
    uintptr_t jdir;
    uint64_t sceProcType;
    uint64_t sonyCred;
    uint64_t sceProcCap;
};

struct jbc_jail_state
{
    struct jbc_cred original;
    struct jbc_cred root;
};

typedef enum KmemKind { USERSPACE, KERNEL_HEAP, KERNEL_TEXT } KmemKind;

static uintptr_t prison0;
static uintptr_t rootvnode;

int jbc_jailbreak(struct jbc_jail_state* state);
int jbc_unjailbreak(struct jbc_jail_state* state);
int jbc_mount_in_sandbox(const char* system_path, const char* mnt_name);
int jbc_unmount_in_sandbox(const char* mnt_name);
