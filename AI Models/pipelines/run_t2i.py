"""Pipeline A: text -> image.png (FLUX.2-klein-4B GGUF Q8).

Run only inside .venv-a. Exit this process before Pipeline B.
Do not use enable_model_cpu_offload (fills 12-16 GB RAM).
"""
import gc
import os
from pathlib import Path

import torch
from huggingface_hub import hf_hub_download
from transformers import AutoTokenizer, Qwen3ForCausalLM
from diffusers import Flux2KleinPipeline, Flux2Transformer2DModel, GGUFQuantizationConfig

ROOT = Path(__file__).resolve().parents[1]
os.environ.setdefault("HF_HOME", str(ROOT / "hf_cache"))

MODEL = "black-forest-labs/FLUX.2-klein-4B"
PROMPT = "a wooden chair, plain white background, product photo"
EMBEDS = ROOT / "prompt_embeds.pt"
OUT = ROOT / "image.png"


def vram(tag: str) -> None:
    torch.cuda.synchronize()
    print(
        f"[VRAM] {tag}: now {torch.cuda.memory_allocated()/1e9:.2f} GB, "
        f"peak {torch.cuda.max_memory_reserved()/1e9:.2f} GB"
    )


def free_gpu() -> None:
    gc.collect()
    torch.cuda.empty_cache()
    torch.cuda.synchronize()


def main() -> None:
    if not torch.cuda.is_available():
        raise SystemExit("CUDA required for Pipeline A")

    Flux2KleinPipeline._execution_device = property(
        lambda self: torch.device("cuda")
    )

    torch.cuda.reset_peak_memory_stats()
    vram("start")
    tok = AutoTokenizer.from_pretrained(MODEL, subfolder="tokenizer")

    if EMBEDS.exists():
        prompt_embeds = torch.load(EMBEDS, map_location="cpu", weights_only=True)
        print("reusing embeds", tuple(prompt_embeds.shape))
    else:
        enc = Qwen3ForCausalLM.from_pretrained(
            MODEL, subfolder="text_encoder", dtype=torch.bfloat16, low_cpu_mem_usage=True
        )
        enc.to("cuda")
        free_gpu()
        vram("encoder on GPU")
        with torch.inference_mode():
            prompt_embeds = Flux2KleinPipeline._get_qwen3_prompt_embeds(
                text_encoder=enc,
                tokenizer=tok,
                prompt=PROMPT,
                device="cuda",
                max_sequence_length=128,
            )
        prompt_embeds = prompt_embeds.detach().cpu().contiguous()
        del enc
        free_gpu()
        torch.save(prompt_embeds, EMBEDS)
        vram("encoder gone")

    gguf = hf_hub_download("unsloth/FLUX.2-klein-4B-GGUF", "flux-2-klein-4b-Q8_0.gguf")
    transformer = Flux2Transformer2DModel.from_single_file(
        gguf,
        quantization_config=GGUFQuantizationConfig(compute_dtype=torch.bfloat16),
        dtype=torch.bfloat16,
        config=MODEL,
        subfolder="transformer",
    )
    pipe = Flux2KleinPipeline.from_pretrained(
        MODEL, transformer=transformer, dtype=torch.bfloat16
    )

    class _DummyTE(torch.nn.Module):
        def __init__(self):
            super().__init__()
            self.dtype = torch.bfloat16
            self.register_buffer("_t", torch.zeros(1))

    pipe.text_encoder = _DummyTE()
    pipe.to("cuda")
    vram("pipeline on GPU")
    with torch.inference_mode():
        image = pipe(
            prompt_embeds=prompt_embeds.to("cuda"),
            height=1024,
            width=1024,
            guidance_scale=1.0,
            num_inference_steps=4,
        ).images[0]
    vram("after generate")
    image.save(OUT)
    print("done ->", OUT)


if __name__ == "__main__":
    main()
