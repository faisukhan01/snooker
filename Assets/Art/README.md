# Art (swap folder)

Structured swap folder for professional art assets (PDF §06: use clean temporary/procedural assets
until final assets exist, without rewriting gameplay code).

Drop-in conventions (no script changes required):
- `balls/` — ball textures per colour (Red/Yellow/Green/Brown/Blue/Pink/Black/Cue, 512² recommended).
- `table/` — baize diffuse + normal maps (1024²+), wood frame textures.
- `cue/` — cue shaft/butt/tip textures.
- `ui/` — optional icon set for Home screen buttons.

Everything gameplay-visible is currently generated procedurally at runtime by
`Assets/Scripts/Physics/TableBuilder.cs` and `Assets/Scripts/UI/Theme.cs`, so this folder can stay
empty and the game still renders the complete coherent visual system.
