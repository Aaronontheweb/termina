#!/usr/bin/env python3
"""Inject a Windows Terminal DA1 reply into a live Termina PTY and assert it is consumed.

Windows Terminal answers the primary device attributes query (CSI c) with a
38-character reply. Termina must consume that reply as a capability response.
If it does not, the parser flushes it as raw key events and the characters land
in the focused text input as typed text.

This harness drives the real conformance demo under a PTY, writes the exact
reply bytes, and then checks the recorded input-text events.
"""
import argparse
import json
import os
import pty
import select
import signal
import subprocess
import sys
import time
from pathlib import Path

# The exact reply Windows Terminal sends for CSI c.
DA1_REPLY = b"\x1b[?61;4;6;7;14;21;22;23;24;28;32;42;52c"

# A short VT100 reply, which the parser already handled before the fix.
SHORT_DA1_REPLY = b"\x1b[?1;2c"


def read_available(fd: int, timeout: float) -> bytes:
    """Drain whatever the child has written, up to timeout seconds."""
    chunks = []
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            break
        ready, _, _ = select.select([fd], [], [], min(remaining, 0.1))
        if not ready:
            continue
        try:
            chunk = os.read(fd, 65536)
        except OSError:
            break
        if not chunk:
            break
        chunks.append(chunk)
    return b"".join(chunks)


def run_case(label: str, reply: bytes, events_path: Path, demo_cmd: list[str]) -> dict:
    """Run the demo under a PTY, inject reply bytes, and return the recorded events."""
    if events_path.exists():
        events_path.unlink()

    master_fd, slave_fd = pty.openpty()
    env = dict(os.environ)
    env["TERM"] = "xterm-256color"
    env.pop("TMUX", None)
    env.pop("TERM_PROGRAM", None)

    proc = subprocess.Popen(
        demo_cmd,
        stdin=slave_fd,
        stdout=slave_fd,
        stderr=slave_fd,
        env=env,
        start_new_session=True,
    )
    os.close(slave_fd)

    try:
        # Wait for the app to start and write its startup event.
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            read_available(master_fd, 0.2)
            if events_path.exists() and events_path.stat().st_size > 0:
                break

        # Let the capability probe run before injecting the reply.
        read_available(master_fd, 1.5)

        # Inject the reply exactly as a terminal would.
        os.write(master_fd, reply)
        read_available(master_fd, 1.5)

        # Quit cleanly so the recorder flushes.
        os.write(master_fd, b"\x11")  # Ctrl+Q
        read_available(master_fd, 1.0)
    finally:
        if proc.poll() is None:
            proc.send_signal(signal.SIGTERM)
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait(timeout=5)
        os.close(master_fd)

    events = []
    if events_path.exists():
        for line in events_path.read_text().splitlines():
            line = line.strip()
            if not line:
                continue
            try:
                events.append(json.loads(line))
            except json.JSONDecodeError:
                # A leaked ESC byte makes the recorder emit invalid JSON. Treat the
                # raw line as evidence of a leak rather than crashing the harness.
                events.append({"phase": "text", "text": line})

    return {"label": label, "reply": reply, "events": events}


def leaked_text(events: list[dict]) -> str:
    """Return the input text the app recorded, ignoring empty events.

    A leaked DA1 reply shows up as a run of TextChanged events that echo the
    reply's own characters. The recorder also emits invalid JSON when a raw ESC
    byte reaches the input, so those lines arrive here as synthetic text events.
    """
    parts = []
    for evt in events:
        if evt.get("phase") == "text":
            text = evt.get("text", "")
            if text:
                parts.append(text)
    return "".join(parts)


def summarize_leak(text: str) -> str:
    """Describe a leak without dumping the whole recorded buffer."""
    if "61;4;6;7" in text:
        return "the Windows Terminal DA1 reply reached the text input"
    if "[?" in text:
        return "a DA1 reply reached the text input"
    return f"unexpected input text: {text[:80]!r}"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--demo", required=True, help="Executable to run (dotnet)")
    parser.add_argument("--demo-arg", action="append", default=[], help="Extra argument for the demo")
    parser.add_argument("--events", required=True, help="Path to events.jsonl")
    parser.add_argument("--artifacts", required=True, help="Directory for artifacts")
    args = parser.parse_args()

    events_path = Path(args.events)
    artifacts = Path(args.artifacts)
    artifacts.mkdir(parents=True, exist_ok=True)

    demo_cmd = [args.demo, *args.demo_arg, "--kitty-alt-scroll"]

    failures = []

    # Case 1: the long Windows Terminal reply must be consumed, not typed.
    long_case = run_case("windows-terminal", DA1_REPLY, events_path, demo_cmd)
    long_leak = leaked_text(long_case["events"])
    (artifacts / "windows-terminal-events.jsonl").write_text(
        "\n".join(json.dumps(e) for e in long_case["events"])
    )

    if long_leak:
        failures.append(
            f"Windows Terminal DA1 reply leaked into input: {summarize_leak(long_leak)}"
        )

    # Case 2: the short VT100 reply must still be consumed.
    short_case = run_case("vt100", SHORT_DA1_REPLY, events_path, demo_cmd)
    short_leak = leaked_text(short_case["events"])
    (artifacts / "vt100-events.jsonl").write_text(
        "\n".join(json.dumps(e) for e in short_case["events"])
    )

    if short_leak:
        failures.append(f"VT100 DA1 reply leaked into input: {summarize_leak(short_leak)}")

    # Both cases must have produced a startup event, or the harness proved nothing.
    for case in (long_case, short_case):
        if not any(e.get("phase") == "startup" for e in case["events"]):
            failures.append(f"{case['label']}: no startup event recorded")

    if failures:
        for failure in failures:
            print(f"ASSERTION FAILED: {failure}", file=sys.stderr)
        return 1

    print("DA1 injection assertions passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
