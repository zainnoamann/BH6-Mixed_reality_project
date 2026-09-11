"""Pipeline A: text -> image.png (FLUX.2-klein-4B GGUF Q8).

Run only inside .venv-a. Exit this process before Pipeline B.
Do not use enable_model_cpu_offload (fills 12-16 GB RAM).

Usage (also called by server/server.py as a subprocess):
    python run_t2i.py --prompt "a wooden chair" --out /path/image.png [--seed 1] [--steps 4]

Progress is reported on stdout as lines the server parses:
    ##PROGRESS <0..1> <stage text>
"""
import argparse
import gc
import hashlib
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
os.environ.setdefault("HF_HOME", str(ROOT / "hf_cache"))

MODEL = "black-forest-labs/FLUX.2-klein-4B"
GGUF_REPO = "unsloth/FLUX.2-klein-4B-GGUF"
GGUF_FILE = "flux-2-klein-4b-Q8_0.gguf"
EMBED_DIR = ROOT / "prompt_embeds"


def progress(value: float, stage: str) -> None:
    print(f"##PROGRESS {max(0.0, min(1.0, value)):.3f} {stage}", flush=True)


def vram(tag: str) -> None:
    import torch

    torch.cuda.synchronize()
    print(
        f"[VRAM] {tag}: now {torch.cuda.memory_allocated()/1e9:.2f} GB, "
        f"peak {torch.cuda.max_memory_reserved()/1e9:.2f} GB",
        flush=True,
    )


def free_gpu() -> None:
    import torch

    gc.collect()
    torch.cuda.empty_cache()
    torch.cuda.synchronize()


def embeds_path(prompt: str) -> Path:
    # One cache file per prompt. A single shared file would silently reuse the
    # embeddings of whatever prompt ran first.
    digest = hashlib.sha1(prompt.strip().lower().encode("utf-8")).hexdigest()[:16]
    EMBED_DIR.mkdir(parents=True, exist_ok=True)
    return EMBED_DIR / f"{digest}.pt"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--prompt", required=True)
    parser.add_argument("--out", required=True, help="output PNG path")
    parser.add_argument("--seed", type=int, default=None)
    parser.add_argument("--steps", type=int, default=4)
    parser.add_argument("--size", type=int, default=1024)
    args = parser.parse_args()

    import torch
    from diffusers import Flux2KleinPipeline, Flux2Transformer2DModel, GGUFQuantizationConfig
    from huggingface_hub import hf_hub_download
    from transformers import AutoTokenizer, Qwen3ForCausalLM

    if not torch.cuda.is_available():
        raise SystemExit("CUDA required for Pipeline A")

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)

    # Product-photo framing gives TripoSR a clean subject to reconstruct.
    prompt = f"{args.prompt}, plain white background, product photo, 3/4 view"

    progress(0.02, "Loading text encoder")

    Flux2KleinPipeline._execution_device = property(lambda self: torch.device("cuda"))

    torch.cuda.reset_peak_memory_stats()
    vram("start")
    tok = AutoTokenizer.from_pretrained(MODEL, subfolder="tokenizer")

    cache = embeds_path(prompt)

    if cache.exists():
        prompt_embeds = torch.load(cache, map_location="cpu", weights_only=True)
        print("reusing embeds", tuple(prompt_embeds.shape), flush=True)
    else:
        enc = Qwen3ForCausalLM.from_pretrained(
            MODEL, subfolder="text_encoder", dtype=torch.bfloat16, low_cpu_mem_usage=True
        )
        enc.to("cuda")
        free_gpu()
        vram("encoder on GPU")
        progress(0.10, "Encoding prompt")
        with torch.inference_mode():
            prompt_embeds = Flux2KleinPipeline._get_qwen3_prompt_embeds(
                text_encoder=enc,
                tokenizer=tok,
                prompt=prompt,
                device="cuda",
                max_sequence_length=128,
            )
        prompt_embeds = prompt_embeds.detach().cpu().contiguous()
        del enc
        free_gpu()
        torch.save(prompt_embeds, cache)
        vram("encoder gone")

    progress(0.20, "Loading image model")

    gguf = hf_hub_download(GGUF_REPO, GGUF_FILE)
    transformer = Flux2Transformer2DModel.from_single_file(
        gguf,
        quantization_config=GGUFQuantizationConfig(compute_dtype=torch.bfloat16),
        dtype=torch.bfloat16,
        config=MODEL,
        subfolder="transformer",
    )
    pipe = Flux2KleinPipeline.from_pretrained(MODEL, transformer=transformer, dtype=torch.bfloat16)

    class _DummyTE(torch.nn.Module):
        def __init__(self):
            super().__init__()
            self.dtype = torch.bfloat16
            self.register_buffer("_t", torch.zeros(1))

    pipe.text_encoder = _DummyTE()
    pipe.to("cuda")
    vram("pipeline on GPU")

    generator = None
    if args.seed is not None:
        generator = torch.Generator(device="cuda").manual_seed(args.seed)

    steps = max(1, args.steps)

    def on_step(pipeline, step, timestep, kwargs):
        progress(0.30 + 0.60 * (step + 1) / steps, f"Generating image ({step + 1}/{steps})")
        return kwargs

    progress(0.30, "Generating image")
    with torch.inference_mode():
        image = pipe(
            prompt_embeds=prompt_embeds.to("cuda"),
            height=args.size,
            width=args.size,
            guidance_scale=1.0,
            num_inference_steps=steps,
            generator=generator,
            callback_on_step_end=on_step,
        ).images[0]

    vram("after generate")
    image.save(out)
    progress(1.0, "Image ready")
    print("done ->", out, flush=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:  # make failures visible to the server log
        print(f"##ERROR {type(exc).__name__}: {exc}", flush=True)
        raise
