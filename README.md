# CreativeTwin

## AI + XR for Interactive 3D Interior Customisation

**COSC2476 Mixed Reality (2650) · Group BH6 · Milestone 2**

CreativeTwin is a Unity-based AI + XR prototype for interactive 3D interior customisation.

The system allows users to select objects within a 3D interior, describe a desired furniture item or change using natural language, generate a new 3D object using AI, place the generated object inside the room, and review the result spatially.

The current prototype runs end-to-end on desktop. AI generation is performed externally through a FastAPI service, while Unity acts as the runtime environment for the interior, user interaction, object placement, and eventual XR review.

> **Current status:** The text-to-3D generation workflow is working end-to-end on desktop. XR headset deployment, automatic scaling, constraint-aware placement, collision checking, and AI-generated texture integration into Unity are still under development.

---

## Overview

Traditional interior customisation often requires repeated manual editing of materials and 3D objects. Exploring multiple design alternatives can therefore require significant time and familiarity with 3D authoring tools.

CreativeTwin explores a natural-language workflow:

```text
User Prompt
     ↓
FLUX.2-klein-4B
     ↓
Reference Image
     ↓
User Review
     ↓
Hunyuan3D-2mini-Turbo
     ↓
3D Mesh (.glb)
     ↓
FastAPI Service
     ↓
Unity Runtime
     ↓
Object Placement
     ↓
Spatial / XR Review
```

The room remains a standardised 3D environment prepared for Unity. Unlike the original Milestone 1 plan, furniture can now be generated dynamically from a natural-language prompt rather than relying only on a fixed asset library.

---

## Features

### 3D Interior

* Interactive 3D interior environment.
* Semantic object representation.
* Objects identified by name.
* Mouse-based hover and selection.
* 360-degree desktop walkthrough.

### Natural-Language Object Generation

Users can request a new furniture object using a natural-language prompt.

Example:

```text
Add a modern wooden bedside table next to the bed.
```

The request is sent from Unity to the external AI generation service.

### AI Text-to-Image Generation

CreativeTwin uses **FLUX.2-klein-4B** to convert a natural-language prompt into a reference image.

The current tested configuration uses:

* Q8 GGUF
* 4 sampling steps
* Approximately 40 seconds on a Google Colab T4
* Approximately 15 GB GPU memory
* Approximately 12.7 GB system memory

The generated image is presented to the user before 3D generation begins.

### AI Image-to-3D Generation

The accepted reference image is passed to **Hunyuan3D-2mini-Turbo**.

The current configuration uses:

* CUDA
* 5 sampling steps
* Octree 256

The resulting object is returned as a `.glb` file and loaded into Unity.

### Review Before Commit

Generated content is not immediately committed to the scene.

The user can:

* Review the generated reference image.
* Regenerate the image.
* Accept the image and continue to 3D generation.
* Review the generated 3D object.
* Accept the generated object.
* Undo or regenerate the result.

This review-first workflow is important because AI generation is probabilistic and generated results can vary.

### Object Manipulation

The current prototype supports:

* Object selection.
* Object movement.
* Floor-constrained dragging.
* Generated-object placement.

Further manipulation functionality, including rotation and deletion, remains under development.

### Context Menu

Selecting an object opens the context menu with four available actions.

#### Add New Object

1. Select **Add New Object** and enter a natural-language prompt.
2. Review the generated image, accept or regenerate it, then review and place the generated 3D object.

#### Change Texture

1. Select **Change Texture** and provide the desired texture or appearance.
2. The AI texture pipeline generates the texture; Unity integration of the generated texture is currently under development.

#### Move Object

1. Select **Move Object** to activate object movement.
2. Drag the object to the desired position while it remains constrained to the floor.

### Fallback Mode

The application includes a fallback mode so that the demonstration flow does not completely fail when the external AI service or GPU is unavailable.

If texture generation fails, the job can still return an untextured mesh rather than terminating the complete request.

---

## System Architecture

CreativeTwin separates the Unity runtime from the AI generation pipeline.

```text
┌──────────────────────────────┐
│         Unity Client         │
│                              │
│  3D Scene                    │
│  User Interface              │
│  Object Selection            │
│  Object Manipulation         │
│  Review / Placement          │
└──────────────┬───────────────┘
               │
               │ HTTP
               ▼
┌──────────────────────────────┐
│       FastAPI Service        │
│                              │
│  Request Handling            │
│  Job Management              │
│  Pipeline Coordination       │
└──────────────┬───────────────┘
               │
               ▼
        ┌──────────────┐
        │    FLUX      │
        │              │
        │ Prompt →     │
        │ Reference    │
        │ Image        │
        └──────┬───────┘
               │
               ▼
        ┌──────────────┐
        │ Review Gate  │
        └──────┬───────┘
               │
               ▼
      ┌────────────────────┐
      │    Hunyuan3D-2     │
      │                    │
      │ Image → 3D Mesh    │
      └──────────┬─────────┘
                 │
                 ▼
        ┌────────────────┐
        │ Hunyuan Paint  │
        │ Texture Stage  │
        └───────┬────────┘
                │
                ▼
              .glb
                │
                ▼
        ┌────────────────┐
        │ Unity Runtime  │
        │                │
        │ Load Object    │
        │ Place Object   │
        │ Review Object  │
        └────────────────┘
```

### Unity as a Thin Client

Unity does not run the generative models.

The models, model weights, and generation processes remain outside the Unity application.

Unity:

1. Collects the user's request.
2. Sends the request to FastAPI.
3. Polls the generation job.
4. Receives the result.
5. Loads the `.glb` object.
6. Handles user review and placement.

This architecture keeps the Unity client lightweight and avoids requiring the eventual XR device to run the generative models.

---

## Runtime AI Pipeline

### 1. Prompt → Image

**FLUX.2-klein-4B**

The user's natural-language request is sent to Pipeline A.

```text
Natural-language prompt
          ↓
FLUX.2-klein-4B
          ↓
image.png
```

The resulting image is presented to the user for review.

### 2. Image → 3D

After the image is accepted:

```text
image.png
     ↓
Hunyuan3D-2mini-Turbo
     ↓
shape.glb
```

The generated mesh is then returned to Unity.

### 3. Texture Generation

CreativeTwin also uses **Hunyuan Paint** to investigate AI-generated textures.

The texture pipeline can produce a baked texture, but the result is currently not reliably reaching Unity.

The main issue is the `custom_rasterizer` CUDA extension required by Hunyuan Paint. Compatibility between the PyTorch, CUDA, Python, and compiler environment is currently preventing reliable integration.

Until this issue is resolved, generated objects use a Unity material fallback.

---

## FastAPI Service

The Unity application communicates with the AI pipeline through a FastAPI service.

The current service uses:

```text
Port: 8765
```

The runtime workflow is:

```text
Unity
  ↓
POST prompt
  ↓
FastAPI
  ↓
Create generation job
  ↓
Unity polls job
  ↓
AI pipelines execute
  ↓
.glb generated
  ↓
Unity downloads result
```

Unity does not directly manipulate the AI pipeline's file system.

---

## AI Pipeline Environments

The AI models are executed as separate subprocesses using separate Python virtual environments.

This is necessary because:

* The model dependencies conflict.
* The models cannot reliably remain loaded together within available memory.
* Shape generation and texture generation can therefore be run independently.

The AI model pipelines and their corresponding requirement files are located in:

```text
AI Models/
```

---

## 3D Environment

The baseline interior is a bedroom scene created using SketchUp and prepared through Blender before being imported into Unity.

The original scene contained approximately 6,000 objects.

A large amount of decorative geometry was removed to make the scene more practical for runtime use. For example, a single vase accounted for approximately 1,968 objects in the original model.

Components were renamed so that Unity can identify them by name.

Some components were imported as merged meshes and therefore cannot currently be addressed independently. Resolving these remaining merged objects is part of the planned development work.

---

## Semantic Object Representation

CreativeTwin does not treat the room as one single undifferentiated mesh.

Objects that need to be interacted with are represented separately so Unity can identify and manipulate them.

The current semantic workflow supports:

```text
3D Environment
      ↓
Individual Objects
      ↓
Object Names / Identity
      ↓
Unity Selection
      ↓
Interaction Menu
```

This structure is important for future constraint checking because the system needs to know the identity, location, dimensions, and relationships of objects within the room.

---

## Current Interaction Flow

The current desktop prototype follows this workflow:

### Step 1 — Select

The user selects an object in the Unity scene.

### Step 2 — Open Interaction Menu

The selected object exposes the available interaction actions.

### Step 3 — Generate

The user chooses **Add New Object** and enters a natural-language request.

### Step 4 — Review Image

FLUX generates a reference image.

The user can accept or regenerate the image.

### Step 5 — Generate 3D

Hunyuan3D-2mini-Turbo generates the corresponding 3D mesh.

### Step 6 — Review and Place

The generated object is loaded into Unity and appears in front of the user.

The user can drag it along the floor and review its position.

### Step 7 — Accept

The user accepts the result or chooses to undo/regenerate.

### Step 8 — Explore

The user can walk through the interior and review the generated object in context.

---

## Current Project Status

| Feature                          | Status                   |
| -------------------------------- | ------------------------ |
| Standardised 3D room             | ✅ Working                |
| Semantic object representation   | ✅ Working                |
| Object naming / identification   | ✅ Working                |
| Hover feedback                   | ✅ Working                |
| Mouse-based object selection     | ✅ Working                |
| 360° desktop walkthrough         | ✅ Working                |
| FastAPI service                  | ✅ Working                |
| Natural-language prompting       | ✅ Working                |
| FLUX.2-klein-4B image generation | ✅ Working                |
| Hunyuan3D-2mini-Turbo generation | ✅ Working                |
| Runtime `.glb` loading           | ✅ Working                |
| Generated-object movement        | ✅ Working                |
| Review / regenerate workflow     | ✅ Working                |
| Fallback / placeholder mode      | ✅ Working                |
| Hunyuan Paint texture bake       | ⚠️ Working in isolation  |
| Texture integration into Unity   | ⚠️ Blocked / In progress |
| Object rotation                  | ⚠️ In progress           |
| Object deletion                  | ⚠️ In progress           |
| XR headset build                 | ⚠️ In progress           |
| Real-world object scaling        | ❌ Not implemented        |
| Automatic floor snapping         | ❌ Not implemented        |
| Wall-clearance checking          | ❌ Not implemented        |
| Collision detection              | ❌ Not implemented        |
| Automatic placement              | ❌ Not implemented        |
| Constraint-aware placement       | ❌ Not implemented        |

---

## Research Focus: Constraint-Aware Customisation

The main research contribution of CreativeTwin is not simply generating a 3D object.

The larger question is:

> **Can an AI-generated object be placed appropriately within an existing interior while respecting the spatial constraints of that environment?**

The planned constraint system focuses on three checks.

### 1. Room Boundary

The generated object should fit within the available room.

### 2. Object Overlap

The generated object should not overlap existing furniture or other scene objects.

### 3. Scale Consistency

The generated object should have a believable size relative to the room and surrounding objects.

These checks are **not yet implemented**.

Currently, generated meshes do not provide reliable real-world scale information. Unity therefore applies a forced height and the user manually positions the object.

The remaining development work is to automate these spatial checks so that the system can assist the user with scale, placement, and collision constraints rather than relying entirely on manual judgement.

---

## Model Selection

The project evaluated multiple AI model configurations using the same test object and the prompt:

```text
a wooden dining chair
```

The final pipeline was selected based on what could run reliably on the available hardware rather than only published model quality or VRAM specifications.

| Model                        | Role             | Result                                     |
| ----------------------------- | ---------------- | ------------------------------------------ |
| **FLUX.2-klein-4B, Q8 GGUF** | Pipeline A       | Adopted                                    |
| **Hunyuan3D-2mini-Turbo**    | Pipeline B       | Adopted                                    |
| **TRELLIS.2**                | Alternative      | Dropped due to generation failures         |
| **TripoSR, quantised**       | Fallback         | Retained because it consistently completes |
| **Hunyuan Paint**            | Texture          | Adopted but currently blocked from Unity   |
| **Unity tileable material**  | Texture fallback | Currently used                             |

---

## Performance

The current development measurements were obtained using accessible GPU hardware.

### FLUX.2-klein-4B

Approximately:

```text
40 seconds
```

for the reference image generation stage on a free Google Colab T4.

### Hunyun Paint

The texture bake was reduced from approximately:

```text
20–40 minutes
```

on the free Colab T4 to approximately:

```text
1.5–2 minutes
```

on the available lab GPU.

### Generation Workflow

The complete workflow involves multiple processing stages and therefore includes a noticeable wait between the user's request and the final 3D object.

The interface separates image generation, image review, 3D generation, and final object review so that these processing stages can be communicated to the user.

---

## Requirements

### Unity

The Unity project is developed and tested with:

* **Project:** `BH6-Mixed_reality_project`
* **Unity:** **Unity 6.3 LTS**
* **Editor Version:** `6000.3.20f1`
* **Target Platform:** Windows, Mac, Linux
* **Graphics API:** DX11
* **Main Scene:** `SampleScene`

The project should be opened using **Unity 6.3 LTS (6000.3.20f1)** to ensure compatibility with the project configuration and packages.

### AI Generation

Running the complete AI workflow requires:

* Python
* CUDA-compatible NVIDIA GPU
* Required AI model files
* Python dependencies supplied with the AI pipelines
* Sufficient GPU and system memory

The AI pipelines use separate Python environments because their dependencies cannot reliably share a single environment.

### XR

The current working demonstration is desktop-based.

XR headset deployment and headset-specific interaction are part of the ongoing development.

---

## Installation and Running the Project

Follow these steps to run the complete CreativeTwin prototype from a fresh clone.

### 1. Clone the Repository

```bash
git clone https://github.com/zainnoamann/BH6-Mixed_reality_project.git
cd BH6-Mixed_reality_project
```

### 2. Open the Unity Project

Open the cloned repository through **Unity Hub**.

Select the Unity project folder:

```text
BH6-Mixed_reality_project/
```

Allow Unity to import the project and install the packages listed in the project's `Packages/` configuration.

---

### 3. Download and Set Up the AI Models

The AI generation server is contained in:

```text
AI Models/
```

The AI pipelines use separate Python environments because their dependencies conflict and the models cannot reliably run together in the same environment.

Before running the project, make sure the required AI models and Python dependencies have been downloaded and configured according to the files provided in `AI Models/`.

The AI server requires a CUDA-compatible NVIDIA GPU with sufficient GPU and system memory.

---

### 4. Start the AI Server

Open a terminal and navigate to the AI Models directory:

```bash
cd "AI Models"
```

Start the AI server using:

```bash
python start.py
```

The startup script downloads/initialises the required AI components and starts the FastAPI service.

The Unity application communicates with this service over:

```text
http://localhost:8765
```

Keep this terminal running while using the Unity application.

> **Important:** Start the AI server **before** pressing Play in Unity. If the server is not running, AI object generation will not work and the application may use its fallback/placeholder behaviour.

---

### 5. Open the Correct Unity Scene

After the AI server is running, return to Unity.

Open the main CreativeTwin demonstration scene from the Unity project.

Then press:

```text
Play
```

The application should start in the interactive interior environment.

> **Important:** The exact demonstration scene should be opened from the project's Unity scene files. If multiple scenes are present, use the scene configured for the CreativeTwin desktop demonstration rather than a development/test scene.

---

### 6. Run the AI Object Generation Workflow

Once Unity is running:

1. Select an object in the interior.
2. Open its context menu.
3. Select **Add New Object**.
4. Enter a natural-language prompt.

For example:

```text
a modern wooden bedside table
```

5. Unity sends the prompt to the FastAPI AI server.
6. FLUX.2-klein-4B generates a reference image.
7. Review the generated image.
8. Choose **Accept** to continue or **Regenerate** to create another image.
9. Hunyuan3D-2mini-Turbo generates the 3D mesh.
10. Unity downloads the resulting `.glb`.
11. Review and position the generated object.
12. Accept the object when satisfied with the result.

---

### 7. Using the Other Context Menu Actions

#### Add New Object

1. Select **Add New Object** and enter a natural-language prompt.
2. Review the generated image, accept or regenerate it, then review and place the generated 3D object.

#### Change Texture

1. Select **Change Texture** and provide the desired texture or appearance.
2. The AI texture pipeline generates the texture; Unity integration of the generated texture is currently under development.

#### Move Object

1. Select **Move Object** to activate object movement.
2. Drag the object to the desired position while it remains constrained to the floor.

---

### 8. Shutdown

When finished, stop the Unity application first.

Then return to the terminal running the AI server and stop it with:

```text
Ctrl + C
```

The AI server should be restarted with `python start.py` whenever you want to run the complete generation workflow again.

---

## Quick Start

For developers who have already completed the initial model/dependency setup:

### Terminal

```bash
cd "AI Models"
python start.py
```

### Unity

```text
1. Open the CreativeTwin Unity project
2. Open the main demonstration scene
3. Press Play
4. Select an object
5. Open the context menu
6. Select Add New Object
7. Enter a prompt
8. Review the generated image
9. Accept the image
10. Wait for the generated 3D object
11. Position and review the object
```

The AI server must remain running while Unity is using the generation pipeline.

## Repository Structure

```text
BH6-Mixed_reality_project/
│
├── AI Models/
│   ├── AI generation pipeline files
│   └── Separate dependency requirement files
│
├── Assets/
│   └── Unity assets, scenes, scripts and project content
│
├── Documentation/
│   └── UX_Wireframes/
│
├── Packages/
│   └── Unity package configuration
│
├── ProjectSettings/
│   └── Unity project configuration
│
├── .gitignore
├── .vsconfig
├── CONTRIBUTING.md
└── README.md
```

### `AI Models/`

Contains the AI generation pipelines and their separate dependency requirements.

### `Assets/`

Contains the Unity project's application assets and implementation.

### `Documentation/`

Contains supporting project documentation, including UX wireframes.

### `Packages/`

Contains Unity package configuration.

### `ProjectSettings/`

Contains Unity project configuration.

### `.vsconfig`

Contains Visual Studio configuration information for the Unity development environment.

### `CONTRIBUTING.md`

Documents the repository contribution and development workflow.

---

## Development Workflow

The repository uses a feature-branch workflow with `develop` as the integration branch.

```text
Feature Branch
      ↓
Pull Request
      ↓
Integration Testing
      ↓
develop
```

Feature work is developed separately and merged through pull requests.

Merges into `develop` are integration-tested before acceptance.

This workflow was introduced to reduce conflicts caused by stale branches and locally held work.

---

## Known Limitations

CreativeTwin is a research prototype and is not intended to be a production-ready automated interior design system.

### AI Generation

AI-generated geometry can vary between requests and may contain imperfections.

### Generation Time

The generation process requires significant processing time and cannot currently provide instantaneous results.

### Texture Integration

The Hunyuan Paint texture stage is currently blocked from reliable Unity integration by the `custom_rasterizer` CUDA toolchain issue.

### Object Scale

Generated meshes do not currently contain reliable real-world scale information.

### Object Placement

Generated objects must currently be positioned manually.

### Collision Detection

The system does not yet automatically detect collisions with existing furniture.

### Room Constraints

Room boundaries and available space are not yet automatically evaluated.

### XR

The current demonstration is performed on desktop. Headset deployment and device testing are still in development.

### Semantic Scene Structure

Some imported components remain merged meshes and cannot yet be selected independently.

### Testing

At the current milestone:

* There is no automated test suite.
* Formal user testing has not yet been completed.
* Headset testing has not yet been completed.
* Load/performance testing has not yet been completed.
* Spatial constraint testing cannot begin until the constraint system is implemented.

---

## Roadmap

### Week 9 — Stabilisation

* Resolve the `custom_rasterizer` texture issue.
* Evaluate the Hunyuan3D portable build.
* Serve the models from one shared lab machine.
* Improve error handling.
* Improve cancellation and retry behaviour.
* Continue XR implementation.
* Investigate editing existing objects by prompt.

### Week 10 — Scale and Placement

* Replace forced object height with real-world scale.
* Implement floor snapping.
* Add wall clearance.
* Implement the first collision check against existing furniture.

### Week 11 — Testing and Integration

* Integration testing across the complete workflow.
* XR interaction testing.
* Improve semantic object structure.
* Complete repository documentation.
* Finalise Unity project structure and setup instructions.
* Add screenshots and supporting documentation.

### Week 12 — Finalisation

* Bug fixing.
* Interaction and interface polish.
* Final demonstration video.
* Final testing.
* Final report and submission.

---

## Final Target

The final acceptance condition for CreativeTwin is:

```text
User enters a natural-language request
              ↓
AI generates a furniture object
              ↓
Object receives a believable scale
              ↓
Object is placed in a sensible location
              ↓
Spatial constraints are respected
              ↓
User reviews the result
              ↓
User walks around the result in XR
```

The intended final prototype should allow a user to request a furniture item, receive a textured object at a believable scale and sensible position, and review it inside the room using XR.

---

## Team

| Member                              | Role                        | Main Responsibility                                                        |
| ----------------------------------- | --------------------------- | -------------------------------------------------------------------------- |
| **Dhrumil Pravinbhai Shah**         | Project Lead + AI Architect | AI pipeline, system architecture, integration and project coordination     |
| **Mason (Moe Thwin Nyein Chan)**    | 3D / Environment Lead       | 3D environment, SketchUp, Blender and Unity scene preparation              |
| **Dilrukshi Perera**                | AI Research Engineer        | AI models, model evaluation, generation pipelines and backend              |
| **Hyma Varghese**                   | UX / XR Designer            | UX, XR interaction design, generation/review interfaces                    |
| **Wint Thawdar Linn**               | Unity Developer             | Unity implementation, semantic selection and XR interaction                |
| **Muhammad Zain Ul Abideen Noaman** | Documentation + Testing     | Documentation, repository management, Git workflow and integration testing |

---

## References

* [Unity Documentation](https://docs.unity3d.com/)
* [Unity XR Interaction Toolkit](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@latest/)
* [Microsoft Mixed Reality Documentation](https://learn.microsoft.com/en-us/windows/mixed-reality/)
* [Blender Documentation](https://docs.blender.org/)
* [FastAPI Documentation](https://fastapi.tiangolo.com/)
* [Hunyuan3D-2](https://github.com/Tencent-Hunyuan/Hunyuan3D-2)
* [Hunyuan3D-2 Windows Portable Build](https://github.com/YanWenKun/Hunyuan3D-2-WinPortable)
* [TripoSR](https://github.com/VAST-AI-Research/TripoSR)
* [TRELLIS](https://github.com/microsoft/TRELLIS)
* [ComfyUI](https://github.com/comfyanonymous/ComfyUI)
* [ControlNet](https://github.com/lllyasviel/ControlNet)
* [FLUX](https://github.com/black-forest-labs/flux)
* [StableGen](https://github.com/sakalond/StableGen)
* [Dream Textures](https://github.com/carson-katri/dream-textures)
* [SketchUp Documentation](https://help.sketchup.com/)

---

## Academic Project

**CreativeTwin** is developed by **Group BH6** as part of:

**COSC2476 Mixed Reality (2650)**

The repository contains the Unity application, AI generation pipelines, documentation, and supporting development materials for the project.
