"""Compare speech-to-text models on YOUR recordings.

    python bakeoff.py clips/                       # whisper turbo vs large-v3 vs distil
    python bakeoff.py clips/ --models turbo parakeet

clips/ holds .wav files (say the real prompts: "add a grey sofa", "make the lamp taller").
Optional: next to each clip, a .txt with what you actually said -> word error rate (WER).
Reports per model: load time, avg/max latency, peak VRAM, WER.
"""
import argparse
import re
import time
from pathlib import Path


def norm(s: str) -> list[str]:
    return re.sub(r"[^a-z0-9' ]", " ", s.lower()).split()


def wer(ref: list[str], hyp: list[str]) -> float:
    d = list(range(len(hyp) + 1))
    for i, r in enumerate(ref, 1):
        prev, d[0] = d[0], i
        for j, h in enumerate(hyp, 1):
            prev, d[j] = d[j], min(d[j] + 1, d[j - 1] + 1, prev + (r != h))
    return d[len(hyp)] / max(len(ref), 1)


def vram_mb() -> float:
    try:
        import torch
        return torch.cuda.max_memory_allocated() / 2**20
    except Exception:
        return 0.0


def load_whisper(name):
    from faster_whisper import WhisperModel
    m = WhisperModel(name, device="cuda", compute_type="float16")

    def run(path):
        segs, _ = m.transcribe(path, language="en", beam_size=1, vad_filter=True,
                               condition_on_previous_text=False)
        return " ".join(s.text.strip() for s in segs)
    return run


def load_parakeet():
    import nemo.collections.asr as nemo_asr  # needs its own venv: pip install "nemo_toolkit[asr]"
    m = nemo_asr.models.ASRModel.from_pretrained("nvidia/parakeet-tdt-0.6b-v3")

    def run(path):
        out = m.transcribe([path])
        return getattr(out[0], "text", out[0])
    return run


LOADERS = {
    "turbo": lambda: load_whisper("large-v3-turbo"),
    "large-v3": lambda: load_whisper("large-v3"),
    "distil": lambda: load_whisper("distil-large-v3"),
    "parakeet": load_parakeet,
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("clips")
    ap.add_argument("--models", nargs="+", default=["turbo", "large-v3", "distil"], choices=LOADERS)
    args = ap.parse_args()

    clips = sorted(Path(args.clips).glob("*.wav"))
    if not clips:
        raise SystemExit("no .wav files found")

    for name in args.models:
        try:
            t = time.time()
            run = LOADERS[name]()
            load_s = time.time() - t
        except Exception as exc:
            print(f"\n== {name}: could not load ({exc})")
            continue

        run(str(clips[0]))  # warm-up, not timed
        times, wers = [], []
        print(f"\n== {name}  (load {load_s:.1f}s)")
        for c in clips:
            t = time.time()
            text = run(str(c)).strip()
            dt = time.time() - t
            times.append(dt)
            ref_file = c.with_suffix(".txt")
            tag = ""
            if ref_file.exists():
                w = wer(norm(ref_file.read_text()), norm(text))
                wers.append(w)
                tag = f"  WER {w:.0%}"
            print(f"  {c.name:28s} {dt:5.2f}s  {text!r}{tag}")
        summary = f"  avg {sum(times)/len(times):.2f}s  max {max(times):.2f}s  vram {vram_mb():.0f} MB"
        if wers:
            summary += f"  mean WER {sum(wers)/len(wers):.1%}"
        print(summary)


if __name__ == "__main__":
    main()
