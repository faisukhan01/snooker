# Prefabs (swap folder)

All gameplay objects (balls, table, cue, pockets, UI) are built at runtime by the composition roots
(`MatchSceneRoot`, `HomeSceneRoot`, …), so no prefabs are required to run the game. This folder is
the structured drop-in point if you later want prefab variants (e.g. alternative table skins);
`TableBuilder` and `BallManager` are the single integration points.
