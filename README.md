# CreativeTwin

**AI + XR for Interactive 3D Interior Customisation**

COSC2476 Mixed Reality (2650) · Milestone 1 · 11 August 2026

## Overview

CreativeTwin is a prototype that lets users customise a 3D interior using natural-language AI requests and review the results immersively in XR. Instead of manually editing materials and objects through repetitive clicks and flat 2D previews, users can describe the change they want (e.g. *"Make this wall a warm Scandinavian wood texture"*), have AI generate the update, and explore the result spatially.

## The Problem

Traditional interior customisation workflows are:
- **Manual and repetitive** — every material or object change requires direct editing
- **Slow to compare** — exploring different styles and variations is difficult and time-consuming
- **Non-immersive** — feedback is limited to flat 2D previews, not spatial review

CreativeTwin aims to replace this with natural-language prompts, instant AI-generated variations, and immersive spatial review.

## Project Vision

The pipeline moves from a raw 3D model to an AI-assisted XR experience in five stages:

```
Standardized 3D Model → Semantic Scene → AI Customisation → Unity Runtime → XR Review
```

1. **Standardized 3D Model** — a common input baseline
2. **Semantic Scene** — the model is converted into a scene with identifiable objects and categories
3. **AI Customisation** — natural-language requests propose or generate material and object changes
4. **Unity Runtime** — the central hub connecting semantic objects, AI services, and XR interaction
5. **XR Review** — users explore and evaluate changes spatially

## User Journey

**Load Model → Select Object → Prompt AI → Generate & Update → Review in XR**

1. A standardized 3D interior loads into the system
2. The user selects an object (wall, floor, sofa, bed, etc.)
3. The user issues a natural-language prompt describing the desired change
4. AI generates the modification and Unity applies the update
5. The user explores the result in XR and decides whether to refine it

## Why Unity

Unity is the primary runtime environment for CreativeTwin, responsible for:
- **3D Scene Management** — organising semantic objects, maintaining scene state, applying updates
- **XR Interaction** — object selection, movement/rotation, immersive interaction, real-time visualisation
- **AI Integration** — communicating with external AI services/APIs, sending requests, receiving and applying generated content

## Scope

**Prototype focus:**
- Predefined room types and known furniture/object categories
- Material customisation and basic object manipulation as core targets
- AI-assisted object replacement as an investigation/prototype target (not a completed feature)

**Novelty — Constraint-Aware AI Customisation:** a key challenge this project investigates is ensuring AI-generated or replaced furniture fits naturally within existing room constraints (scale, available space, collision avoidance, spatial consistency) — not just generating an object, but making sure it's appropriate for its environment.

### Project Status

| Confirmed | To Be Validated | Open Research |
|---|---|---|
| Standardized 3D input | 3D model → Unity integration | AI-generated 3D furniture |
| Semantic object representation | Unity ↔ AI communication | Automatic scaling |
| Unity as primary runtime | Runtime material updates | Collision avoidance |
| | XR interaction direction | Automatic placement |
| | Metadata preservation | |

## Roadmap

| Phase | Weeks | Focus |
|---|---|---|
| 1 — Foundation & Feasibility | W1–W3 | Unity setup, 3D model integration, semantic scene |
| 2 — Implementation | W4–W6 | Unity interior scene, object selection, object manipulation |
| 3 — XR & AI Interaction | W7–W9 | XR interaction, immersive walkthrough, AI-assisted material editing |
| 4 — Testing & Refinement | W10–W12 | Integration testing, spatial constraint testing, user interaction testing, final prototype refinement |

## Expected Deliverable

- Interactive 3D interior with semantic object selection
- AI-assisted material customisation
- Basic object manipulation
- Investigation/prototype of AI-assisted object replacement
- XR exploration and review

The goal is a **feasible, demonstrable prototype** — not a fully automated interior design system.

## Team

| Member | Role | Main Responsibility |
|---|---|---|
| Dhrumil Pravinbhai Shah | Project Lead + AI Architect | AI pipeline, integration, project management |
| Moe Thwin Nyein Chan | 3D / Environment Lead | 3D models, Blender, Unity environment |
| Dilrukshi Perera | AI Research Engineer | AI models, research, backend |
| Hyma Varghese | UX / XR Designer | UX, XR experience, presentation |
| Wint Thawdar Linn | Unity Developer | Unity development, XR interaction |
| Muhammad Zain Ul Abideen Noaman | Documentation + Testing | Reports, testing, GitHub support |

## References

- [Unity Documentation](https://docs.unity3d.com/)
- [Microsoft Mixed Reality Documentation](https://learn.microsoft.com/en-us/windows/mixed-reality/)
- [Unity XR Interaction Toolkit Documentation](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@latest)
- [Blender Documentation](https://docs.blender.org/)
- [OpenAI API Documentation](https://platform.openai.com/docs/)
