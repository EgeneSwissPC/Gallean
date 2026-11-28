# Race art pipeline (new units in the Disciples II style)

Status: research, nothing built. First step towards a race creator (a new "water people" race).

## Decisions so far
- Start with graphics, before the race creator screen and before the rules side.
- Local, open-source tools only (no cloud image or 3D services).
- Base the first race on the existing Sea Creatures / Marsh Folk art (Mermaid, Kraken, Sea Serpent, Lizard Man).

## How the game stores unit art (checked in code and in `assets-v1.galleanpack`)
- One pack file: `GALLEANPACK` magic, format 27, a 32-byte header, then a JSON manifest (`Gallean.Assets/AssetManifest.cs`): 2,190 sprites, 11,258 animations, 7,128 sounds. Images are WebP blobs.
- The installed pack is the "enhanced" one: frames are 4x the original resolution (`PixelScale` 4) and each battle frame has a **Normal** map layer (`AssetSpriteLayerType` Normal / Depth / Albedo) that the client uses for lighting.
- **Overlay packs already work:** `AssetProvider.PackLoader.SelectPacks` loads one base pack plus every pack whose manifest says `Sparse: true`, ordered by date. A new race can ship as its own small sparse pack; the 4 GB base pack stays untouched. `AssetPack.Writer` already exists to write packs.
- Per unit (unit ID `g000uuXXXX`), measured on the Mermaid `g000uu5126`:
  - battle: `battle-unit/<id>/{idle,attack,hit}/{attacker,defender}` = 14 + 35 + 12 frames, two facings -> 122 frames, plus the same again as `battle-unit-shadow` and (some units) `battle-unit-aura`;
  - world map: `stack-unit/<id>/{move,stop}/0..7` (8 directions) = 8 x (12 + 16) = 224 frames, plus `stack-unit-shadow` (leaders only);
  - stills: `unit-face` (140x170), `unit-portrait` (260x284), `unit-event-portrait` (700x900), painted.
  - So one leader is roughly 350 animated frames (700 with shadows) and 3 paintings. A soldier has no world-map frames: ~120 battle frames + stills.
- Style: the battle and map frames are **pre-rendered 3D** (3D models rendered from fixed cameras, then dark painterly textures). Faces and portraits are **hand-painted**. That matches the plan "2D design -> 3D model -> render frames".

## Pipeline (all local)
1. **Concept image (2D):** a local image model (FLUX.1 / SDXL in ComfyUI), with existing unit portraits as style references (IP-Adapter or a small LoRA trained on the game's portraits). Gives the front/side concept and later the face/portrait paintings.
2. **Image -> 3D model:** Hunyuan3D-2.1 (shape + texture) or TRELLIS. Output: a textured mesh, usually needs cleanup in Blender.
3. **Rigging:** UniRig (automatic skeleton + skin weights) or Blender Rigify by hand.
4. **Animation:** idle / attack / hit (battle) and walk / stand (map). Hand-keyed in Blender, or retargeted from existing open motion sets. Biggest manual part.
5. **Rendering:** a Blender Python script with the game's cameras: battle camera for attacker/defender facings, isometric camera turned in 8 steps for the map. Renders colour, shadow pass and a normal pass (the game wants Normal layers) at the same frame counts, offsets and anchor points as existing units.
6. **Packing:** a small tool on top of `AssetPack.Writer` writes a sparse pack with the new IDs; plus `Gunits` / text rows for the new units.

## Hardware
- This server: 8 CPU cores, 15 GB RAM, **no GPU**. Fine for steps 5 and 6 (Blender renders on CPU) and for the packing tool.
- GPU needs (from the projects' READMEs):
  - Hunyuan3D-2.1: ~10 GB VRAM shape only, ~21 GB with texture, 29 GB both in one go.
  - TRELLIS: 16 GB+ (users report OOM on 12 GB).
  - FLUX: 12 GB comfortable, 8 GB with quantised (GGUF) weights; SDXL: 8 GB.
  - UniRig: a CUDA GPU (exact figure not checked yet).
- CPU only: image generation works but slowly (minutes per image); image-to-3D models with texture are not practical on CPU; rigging models expect CUDA.
- Spare GTX 970 (4 GB) and GTX 750 Ti (2 GB): too little VRAM, and Maxwell cards are dropping out of current PyTorch CUDA builds. Not usable for steps 1-3.
- Realistic minimum: a 16 GB card (RTX 4060 Ti 16 GB / 5060 Ti 16 GB): concepts, TRELLIS, Hunyuan shape, texture in low-VRAM mode. Comfortable: a 24 GB card (used RTX 3090): everything including Hunyuan texture. On XCP-ng the card goes to a Linux VM by PCI passthrough.

## Phases (rough sizes)
1. ~~**Asset tools, no screen (small, CPU):** extract a unit's frames + normals + stills from the pack to PNG folders with a JSON of metadata; write a sparse pack back from such a folder. Proof: repack the Mermaid as a fake new unit ID and see it in the battle sandbox. This phase has no GPU need; its visible result is that sandbox check.~~ Done, see below.
2. **Render rig (medium, CPU):** Blender script that reproduces the game's battle and map cameras, lighting and frame layout; validate by rendering any free 3D model and comparing it side by side with a real unit in the game.
3. **First unit (large, needs a GPU):** concept -> mesh -> rig -> 3 battle animations -> render -> pack, one soldier first (battle only, ~120 frames + 3 stills).
4. **Leader + map sprites (medium):** add walk/stand and 8 directions.
5. **Rest of the race:** soldier lines, capital art, buildings, land tiles (separate spec when we get there).

## Phase 1 — done (commit 70a13194, branch upgrade/race-art)
- `tools/RaceArt/` (Python stdlib, run with `python3 -I`): `galleanpack.py` (reader + writer for format 27, SHA-256 blob dedupe like `AssetPack.Writer`, WebP size reader), `extract_unit.py <pack> <unit id> <dir>` (all stills, animations, frames and Normal/Depth/Albedo layers as WebP + `unit.json`), `build_pack.py <out> <dir>[=<target id>] ...` (sparse pack; re-reads pixel sizes from the WebP files and checks the aspect ratio against the logical size, so edited images can be dropped in).
- Tests: Mermaid `g000uu5126` extracted (3 stills, 46 animations, 762 frames incl. shadows/auras) and repacked as `g000uu0001`: all 762 frames byte-identical with identical metadata to the base pack.
- Real client check (build merge-0171, base pack + overlay in `~/.local/share/GalleanUpgrade/Assets/`): Mermaid art packed onto the Pegasus Knight (`g000uu0019`). Battle sandbox leader list shows the Mermaid face for the Pegasus Knight, and a watched battle draws the Mermaid's battle animation in its place. No game code change needed; the overlay replaces by asset ID.
- Known limits: no tool yet for new unit IDs (needs Gunits/text rows, part of the race creator); world-map sprites only matter for leaders.

## Open questions
- ~~A. GPU~~: the user chose to **rent hourly** (open-source tools still, rented machine only for the GPU steps). Prices read 2026-11-27 from price trackers: Vast.ai RTX 3090 from ~$0.09-0.20/h, RTX 4090 ~$0.27/h; RunPod Community Cloud RTX 4090 $0.34/h (Secure Cloud $0.89/h). Both bill per second, give SSH and have a CLI/API, so the agent can start a box, run a job and stop it. Salad (~$0.09-0.33/h) is batch containers without a normal SSH box; Hetzner GEX is monthly dedicated, not hourly. Pick: Vast.ai for price, RunPod for reliability; template image with ComfyUI + Hunyuan3D/TRELLIS + Blender, models on a persistent volume so start-up takes minutes.
- B. Faithful look vs new look: match the original dark painted style closely (recommended, the units must sit next to existing ones), or allow a fresher style?
- C. First unit: a new soldier made from scratch, or a re-skin/re-pose of the Mermaid to prove the pipeline end-to-end faster? (Recommend the re-skin first, then from scratch.)
- D. Animation source: hand-keyed in Blender by us, or retargeted motion sets? (Recommend retargeting for walk/idle, hand-keyed attacks.)
- E. Can phase 1 start now on this server (no GPU needed)?

## First unit — progress (Tide Warden, branch upgrade/race-art)
- GPU box: Vast.ai, RTX 4090 48 GB, 150 GB disk, ComfyUI template. Setup scripts and job scripts in `tools/RaceArt/gpu/` (setup.sh, setup_h3d.sh, setup_unirig.sh; gen.py FLUX concepts, to3d.py Hunyuan3D, preview.py turntable, animate.py animation + battle render, warden1_bones.json).
- Timings: 4 FLUX-schnell concepts 20 s; Hunyuan3D-2.1 shape + PBR texture ~220 s (peak 25 GB VRAM); UniRig skeleton + skin ~2 min; 96 battle frames (idle 16, attack 24, hit 8, both sides) ~8 min in Cycles.
- animate.py writes motion in world axes (converted per bone, so UniRig's bone roll doesn't matter), makes the weapon rigid (weapon bones merged into the hand bone, plus every vertex near the weapon line captured, because the automatic skin gave the lower shaft to the legs), renders attackers from the front and defenders from behind like the original frames.
- `tools/RaceArt/pack_render.py` packs the frames onto a unit: the foot goes on the game's anchor (bottom centre of the animation box, `SpriteAtlasSource.CreateAnchoredTransform`), the old unit's shadow/aura animations are blanked, portrait and face come from the concept.
- Real client check (merge-0171 build, overlay on the Pegasus Knight g000uu0019): the Tide Warden stands, attacks and gets hit in the battle sandbox; the leader grid shows the new face.
- Still to do: figure ~1.5x bigger; a ground shadow; Normal layer (lighting); event portrait (the big battle portrait still shows the Pegasus Knight); animation polish; a real unit ID instead of replacing the Pegasus Knight.
