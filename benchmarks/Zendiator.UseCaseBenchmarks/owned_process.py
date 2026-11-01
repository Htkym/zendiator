"""Contain commands before they can spawn children; never kill a stale PID.

Windows uses a Job without breakaway and a stdin-gated Python launcher. Linux
uses a new session; commands must not deliberately escape it with setsid().
Other POSIX platforms are refused until their membership query is implemented.
"""

import ctypes
import os
import re
import signal
import subprocess
import sys
import time
from pathlib import Path



def validate_process_identity(identity):
    if (not isinstance(identity, dict) or type(identity.get("pid")) is not int or identity["pid"] <= 0 or
        identity.get("platform") not in ("windows", "linux") or not isinstance(identity.get("startToken"), str)):
        raise RuntimeError("Missing or incomplete recorded process identity; do not resume")
    token = identity["startToken"]
    pattern = r"[0-9]+" if identity["platform"] == "windows" else r"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}:[0-9]+"
    if not re.fullmatch(pattern, token):
        raise RuntimeError("Unidentifiable recorded process start identity; do not resume")


def linux_snapshot_from_stat(pid, stat, boot):
    try:
        fields = stat[stat.rfind(")") + 2:].split()
        if int(stat.split(" ", 1)[0]) != pid:
            raise ValueError("PID changed")
        identity = {"pid": pid, "platform": "linux", "startToken": boot.strip() + ":" + str(int(fields[19]))}
        validate_process_identity(identity)
        return dict(identity, state="terminated" if fields[0] in ("Z", "X") else "running")
    except (ValueError, IndexError) as error:
        raise RuntimeError(f"Cannot identify process {pid}") from error


def process_snapshot(pid):
    """Read a process through a handle or /proc; never signal a PID."""
    if type(pid) is not int or pid <= 0:
        raise RuntimeError("Invalid process PID")
    if os.name == "nt":
        from ctypes import wintypes as w
        api = ctypes.WinDLL("kernel32", use_last_error=True)
        api.OpenProcess.argtypes, api.OpenProcess.restype = [w.DWORD, w.BOOL, w.DWORD], w.HANDLE
        api.GetProcessTimes.argtypes = [w.HANDLE] + [ctypes.POINTER(w.FILETIME)] * 4
        api.GetProcessTimes.restype = w.BOOL
        api.WaitForSingleObject.argtypes, api.WaitForSingleObject.restype = [w.HANDLE, w.DWORD], w.DWORD
        api.CloseHandle.argtypes, api.CloseHandle.restype = [w.HANDLE], w.BOOL
        handle = api.OpenProcess(0x1000 | 0x100000, False, pid)  # QUERY_LIMITED_INFORMATION | SYNCHRONIZE
        if not handle:
            error = ctypes.get_last_error()
            if error == 87:  # ERROR_INVALID_PARAMETER: PID no longer exists.
                return None
            raise RuntimeError(f"Cannot identify process {pid}; WinError {error}")
        try:
            creation, exit_time, kernel, user = (w.FILETIME() for _ in range(4))
            if not api.GetProcessTimes(handle, ctypes.byref(creation), ctypes.byref(exit_time), ctypes.byref(kernel), ctypes.byref(user)):
                raise RuntimeError(f"Cannot read process creation time; WinError {ctypes.get_last_error()}")
            wait = api.WaitForSingleObject(handle, 0)
            if wait not in (0, 258):
                raise RuntimeError("Cannot determine process execution state")
            ticks = ((creation.dwHighDateTime << 32) | creation.dwLowDateTime) + 504911232000000000
            return {"pid": pid, "platform": "windows", "startToken": str(ticks),
                    "state": "terminated" if wait == 0 else "running"}
        finally:
            api.CloseHandle(handle)
    if sys.platform.startswith("linux") and Path("/proc/self/stat").exists():
        try:
            stat = Path(f"/proc/{pid}/stat").read_text()
        except FileNotFoundError:
            return None
        except OSError as error:
            raise RuntimeError(f"Cannot identify process {pid}") from error
        try:
            boot = Path("/proc/sys/kernel/random/boot_id").read_text()
            return linux_snapshot_from_stat(pid, stat, boot)
        except (OSError, ValueError, IndexError) as error:
            raise RuntimeError(f"Cannot identify process {pid}") from error
    raise RuntimeError("Process identity is unsupported on this platform")


def process_identity(pid):
    snapshot = process_snapshot(pid)
    if snapshot is None or snapshot["state"] != "running":
        raise RuntimeError("Owned launcher exited before its identity was recorded")
    return {name: snapshot[name] for name in ("pid", "platform", "startToken")}


def process_alive(identity):
    validate_process_identity(identity)
    platform = "windows" if os.name == "nt" else "linux" if sys.platform.startswith("linux") else None
    if identity["platform"] != platform:
        raise RuntimeError("Cannot identify recorded process on this platform; do not resume")
    snapshot = process_snapshot(identity["pid"])
    if snapshot is None or snapshot["state"] == "terminated":
        return False
    if snapshot["platform"] != identity["platform"]:
        raise RuntimeError("Cannot identify recorded process on this platform; do not resume")
    return snapshot["startToken"] == identity["startToken"]


class _WindowsJob:
    def __init__(self):
        from ctypes import wintypes as w

        class Limits(ctypes.Structure):
            _fields_ = [("UserTime", ctypes.c_int64), ("JobTime", ctypes.c_int64),
                        ("Flags", w.DWORD), ("MinWorkingSet", ctypes.c_size_t),
                        ("MaxWorkingSet", ctypes.c_size_t), ("ActiveLimit", w.DWORD),
                        ("Affinity", ctypes.c_size_t), ("Priority", w.DWORD),
                        ("Scheduling", w.DWORD)]

        class Io(ctypes.Structure):
            _fields_ = [(name, ctypes.c_uint64) for name in
                        ("ReadOps", "WriteOps", "OtherOps", "ReadBytes", "WriteBytes", "OtherBytes")]

        class ExtendedLimits(ctypes.Structure):
            _fields_ = [("Basic", Limits), ("Io", Io), ("ProcessMemory", ctypes.c_size_t),
                        ("JobMemory", ctypes.c_size_t), ("PeakProcessMemory", ctypes.c_size_t),
                        ("PeakJobMemory", ctypes.c_size_t)]

        class Accounting(ctypes.Structure):
            _fields_ = [(name, ctypes.c_int64) for name in
                        ("UserTime", "KernelTime", "PeriodUserTime", "PeriodKernelTime")] + [
                        (name, w.DWORD) for name in
                        ("PageFaults", "TotalProcesses", "ActiveProcesses", "TerminatedProcesses")]

        self.accounting = Accounting
        self.api = ctypes.WinDLL("kernel32", use_last_error=True)
        signatures = {
            "CreateJobObjectW": ([ctypes.c_void_p, w.LPCWSTR], w.HANDLE),
            "SetInformationJobObject": ([w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD], w.BOOL),
            "QueryInformationJobObject": ([w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD,
                                           ctypes.c_void_p], w.BOOL),
            "OpenProcess": ([w.DWORD, w.BOOL, w.DWORD], w.HANDLE),
            "AssignProcessToJobObject": ([w.HANDLE, w.HANDLE], w.BOOL),
            "TerminateJobObject": ([w.HANDLE, w.UINT], w.BOOL),
            "CloseHandle": ([w.HANDLE], w.BOOL),
        }
        for name, (arguments, result) in signatures.items():
            function = getattr(self.api, name)
            function.argtypes, function.restype = arguments, result
        self.handle = self.api.CreateJobObjectW(None, None)
        if not self.handle:
            raise ctypes.WinError(ctypes.get_last_error())
        limits = ExtendedLimits()
        limits.Basic.Flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; no breakaway.
        if not self.api.SetInformationJobObject(self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            error = ctypes.WinError(ctypes.get_last_error())
            self.close()
            raise error

    def assign(self, pid):
        handle = self.api.OpenProcess(0x0100 | 0x0001, False, pid)  # SET_QUOTA | TERMINATE
        if not handle:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            if not self.api.AssignProcessToJobObject(self.handle, handle):
                raise ctypes.WinError(ctypes.get_last_error())
        finally:
            self.api.CloseHandle(handle)

    def active(self):
        value = self.accounting()
        if not self.api.QueryInformationJobObject(self.handle, 1, ctypes.byref(value),
                                                  ctypes.sizeof(value), None):
            raise ctypes.WinError(ctypes.get_last_error())
        return value.ActiveProcesses

    def terminate(self):
        if not self.api.TerminateJobObject(self.handle, 130):
            raise ctypes.WinError(ctypes.get_last_error())

    def close(self):
        if self.handle:
            self.api.CloseHandle(self.handle)
            self.handle = None


class OwnedCommand:
    def __init__(self, args, cwd, env, log):
        self.args, self.cwd, self.env, self.log = args, cwd, env, log
        self.process = None
        self.job = None
        self.assigned = False
        self.kind = "windows-job" if os.name == "nt" else "linux-session"

    def start(self):
        if os.name == "nt":
            self.job = _WindowsJob()
            self.process = subprocess.Popen(
                [sys.executable, str(Path(__file__).resolve()), "--owned-child", *self.args],
                cwd=self.cwd, env=self.env, stdin=subprocess.PIPE,
                stdout=self.log, stderr=subprocess.STDOUT,
                creationflags=subprocess.CREATE_NEW_PROCESS_GROUP)
            self.job.assign(self.process.pid)
            self.assigned = True
        elif sys.platform.startswith("linux") and Path("/proc/self/stat").exists():
            self.process = subprocess.Popen(
                [sys.executable, str(Path(__file__).resolve()), "--owned-child", *self.args],
                cwd=self.cwd, env=self.env, stdin=subprocess.PIPE,
                stdout=self.log, stderr=subprocess.STDOUT, start_new_session=True)
        else:
            raise RuntimeError("Cannot verify command containment on this platform; refusing launch")

    def release(self):
        if self.process.stdin is not None:
            # The launcher cannot spawn the command until containment and marker persistence succeed.
            self.process.stdin.write(b"1")
            self.process.stdin.flush()
            self.process.stdin.close()

    def _linux_groups(self):
        groups = set()
        for directory in Path("/proc").iterdir():
            if not directory.name.isdigit():
                continue
            try:
                text = (directory / "stat").read_text()
            except FileNotFoundError:
                continue
            fields = text[text.rfind(")") + 2:].split()
            if int(fields[3]) == self.process.pid and fields[0] not in ("Z", "X"):
                groups.add(int(fields[2]))
        return groups

    def empty(self):
        if self.process is None:
            return True
        if self.job:
            return self.assigned and self.job.active() == 0
        return not self._linux_groups()

    def wait_empty(self, timeout):
        deadline = time.monotonic() + timeout
        while not self.empty():
            if time.monotonic() >= deadline:
                return False
            time.sleep(0.05)
        return True

    def stop_and_wait(self, timeout=20):
        # One shared deadline: callers can reserve a bounded cleanup window.
        deadline = time.monotonic() + timeout
        if self.process is None:
            return True
        if self.job:
            if self.assigned:
                self.job.terminate()
            else:
                # Failed Job assignment: the unreleased launcher has never spawned a command.
                self.process.kill()
            self.process.wait(timeout=max(0, deadline - time.monotonic()))
            return self.job.active() == 0 or self.wait_empty(max(0, deadline - time.monotonic()))
        for sig in (signal.SIGTERM, signal.SIGKILL):
            phase_end = min(deadline, time.monotonic() + timeout / 2) if sig == signal.SIGTERM else deadline
            while True:
                self.process.poll()  # Reap our direct child; zombies do not execute.
                groups = self._linux_groups()
                if not groups:
                    return True
                for group in groups:
                    try:
                        os.killpg(group, sig)
                    except ProcessLookupError:
                        pass
                if time.monotonic() >= phase_end:
                    break
                time.sleep(0.05)
        return False

    def close(self):
        if self.process is not None and self.process.stdin is not None:
            self.process.stdin.close()
        if self.job:
            self.job.close()


if __name__ == "__main__":
    if len(sys.argv) < 3 or sys.argv[1] != "--owned-child":
        raise SystemExit("This launcher is private to OwnedCommand")
    if sys.stdin.buffer.read(1) != b"1":
        raise SystemExit("Command was not released into its owned scope")
    raise SystemExit(subprocess.call(sys.argv[2:]))
