# SpriggullYeeter

A MelonLoader VR physics mod for **A Township Tale** (PCVR Mono) built directly on top of **[RagdollLib](https://github.com/JonathanDonkerlot/RagdollLib)**.

Grab any live **Spriggull** with both VR hands, hold it while it squirms and squawks in panic, and **yeet** it across the clearing using realistic VR throw momentum—triggering **RagdollLib** mid-flight so the Spriggull tumbles on impact, bowls into other creatures, and gets back up if it survives.

---

## Required Dependency

- **[RagdollLib](https://github.com/JonathanDonkerlot/RagdollLib)** (`RagdollLib.dll`) is **required** (`[assembly: MelonAdditionalDependencies("RagdollLib")]`).
- Both **RagdollLib** and **SpriggullYeeter** must be installed on **both the client and the server** (`Mods/RagdollLib.dll` + `Mods/SpriggullYeeter.dll`).

---

## Features

- **VR Grab & Carry (1-Hand or 2-Hand)**:
  - Reach out with one or both empty VR hands within `GrabRadius` (`0.6m`) of a live Spriggull and hold **Grip** (set `RequireTwoHands = true` in config if you want two hands required).
  - Suspends Spriggull AI navigation and beak pecking while carried, smoothly tracks your hand (or midpoint of both hands), triggers subtle controller haptics, and plays native panic squawks (`SpriggullFlail`).
  - Safely drops the Spriggull if you spread both hands wider than `MaxHandSeparation` (`1.1m`) or if the Spriggull dies/despawns mid-grab.
- **Artifact-Free VR Throw Physics (`RagdollLib` on Throw Only)**:
  - Uses an ~80ms rolling position/velocity ring buffer (`VelocityRingBuffer`) so relaxing your grip fingers on release does not kill throw momentum.
  - Invokes `RagdollLib.API.RagdollAPI.TriggerRagdoll` **only** when you throw/yeet the Spriggull, firing a loud squawk (`SpriggullAlert`) and native feather/hit particle burst.
- **Spriggull Bowling & Impact Damage**:
  - Tracks collisions across all active `RagdollLib` bone `Rigidbody`s (including detached pelvis bones).
  - High-speed impacts (`>= MinImpactDamageSpeed`) inflict velocity-scaled blunt damage to both the thrown Spriggull and any other creature it bowls into—ragdolling struck creatures too.

---

## Installation (Client & Server)

1. Install **MelonLoader v0.7.3+** on *A Township Tale* (client and/or server).
2. Install **[RagdollLib](https://github.com/JonathanDonkerlot/RagdollLib)** by placing `RagdollLib.dll` in the `Mods/` folder on **both client and server**.
3. Download `SpriggullYeeter.dll` from the [Releases](../../releases) page and place it in `Mods/` on **both client and server**.
4. Launch the game/server once to generate `[SpriggullYeeter]` preferences in `UserData/MelonPreferences.cfg`.

---

## VR Controls

- **Grab**: Move one or both empty hands close to a live Spriggull (`<= 0.6m`) and press & hold **Grip**.
- **Aim & Carry**: Carry with 1 hand or hold with both hands; the Spriggull squirms without entering your inventory.
- **Yeet**: Whip your arm(s) in the desired direction and release Grip to launch into `RagdollLib` flight.
- **Gentle Drop**: Release Grip slowly or pull both hands apart past `1.1m`.

---

## Configuration (`UserData/MelonPreferences.cfg`, `[SpriggullYeeter]`)

| Setting | Default | Description |
|---|---|---|
| `RequireTwoHands` | `false` | Require both VR hands gripping simultaneously (`false` allows 1-hand or 2-hand grabs) |
| `GrabRadius` | `0.60` | Maximum distance (meters) to grab a live Spriggull |
| `MaxHandSeparation` | `1.10` | Maximum hand spread (meters) before auto-releasing a held Spriggull |
| `YeetForceMultiplier` | `1.60` | Throw velocity multiplier applied on release |
| `RagdollDurationSeconds` | `3.50` | Seconds a thrown Spriggull stays ragdolled before recovering if alive |
| `EnableImpactDamage` | `true` | Enables velocity-scaled impact and Spriggull-bowling damage |
| `MinImpactDamageSpeed` | `4.50` | Minimum collision speed (m/s) required to deal impact damage |
| `ImpactDamageMultiplier` | `1.00` | Multiplier applied to impact/bowling damage |
| `EnableDebugLogging` | `false` | Enables verbose `[SpriggullYeeter]` diagnostic logs |

---

## Building from Source

```powershell
dotnet build SpriggullYeeter.sln -c Release
dotnet test SpriggullYeeter.sln -c Release --no-build
```

The standalone output assembly is built at `SpriggullYeeter/bin/Release/net472/SpriggullYeeter.dll` (referencing `RagdollLib.dll` without bundling it). See [ARCHITECTURE.md](ARCHITECTURE.md) for verified game assembly details and state diagrams.
