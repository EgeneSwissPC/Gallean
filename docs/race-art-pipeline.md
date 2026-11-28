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
1. **Asset tools, no screen (small, CPU, this server):** extract a unit's frames + normals + stills from the pack to PNG folders with a JSON of metadata; write a sparse pack back from such a folder. Proof: repack the Mermaid as a fake new unit ID and see it in the battle sandbox. This phase has no GPU need; its visible result is that sandbox check.
2. **Render rig (medium, CPU):** Blender script that reproduces the game's battle and map cameras, lighting and frame layout; validate by rendering any free 3D model and comparing it side by side with a real unit in the game.
3. **First unit (large, needs a GPU):** concept -> mesh -> rig -> 3 battle animations -> render -> pack, one soldier first (battle only, ~120 frames + 3 stills).
4. **Leader + map sprites (medium):** add walk/stand and 8 directions.
5. **Rest of the race:** soldier lines, capital art, buildings, land tiles (separate spec when we get there).

## Open questions
- A. GPU: buy/borrow a 16-24 GB card for the desktop or a VM, or rent? (Recommend a used RTX 3090 24 GB in a passthrough VM; renting conflicts with "local only".)
- B. Faithful look vs new look: match the original dark painted style closely (recommended, the units must sit next to existing ones), or allow a fresher style?
- C. First unit: a new soldier made from scratch, or a re-skin/re-pose of the Mermaid to prove the pipeline end-to-end faster? (Recommend the re-skin first, then from scratch.)
- D. Animation source: hand-keyed in Blender by us, or retargeted motion sets? (Recommend retargeting for walk/idle, hand-keyed attacks.)
- E. Can phase 1 start now on this server (no GPU needed)?
