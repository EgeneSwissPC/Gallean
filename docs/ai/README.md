# World AI

How the world-map AI plays a turn (`src/Gallean.Engine/World/Ai`).

| File | Contents |
| --- | --- |
| `GalleanAI.uxf` | UMLet diagram: turn loop, orders, economy and army actions, scoring, what the AI knows |
| `GalleanAI.pdf`, `GalleanAI.png` | Exports of the diagram |
| `ai_flow.py` | Generator for the diagram (edit this, not the `.uxf`) |
| `notes-custom-map-ai.md` | Notes: why AI leaders stay in the capital on custom maps |

Regenerate after changing `ai_flow.py`:

```sh
python3 ai_flow.py GalleanAI.uxf
java -Djava.awt.headless=true -jar umlet.jar -action=convert -format=pdf -filename=GalleanAI.uxf
java -Djava.awt.headless=true -jar umlet.jar -action=convert -format=png -filename=GalleanAI.uxf
```

UMLet 15.1 standalone: https://www.umlet.com
