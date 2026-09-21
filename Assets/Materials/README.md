# Materials (swap folder)

Runtime materials are constructed procedurally (`TableBuilder.MakeUrp` / `MakeTransparentUnlit`,
`BallManager` PhysicMaterials) so this folder ships intentionally light. To swap in authored
materials later, place `.mat` assets here and reference them from `TableBuilder` — the build path
already checks for registered materials before falling back to procedural ones.
