# Vendored: Google Material Color Utilities (Java)

Files under `hct/`, `palettes/`, `contrast/`, `dislike/`, `temperature/`, `dynamiccolor/`, `scheme/`,
and `utils/` in this directory are vendored from
https://github.com/material-foundation/material-color-utilities, pinned to commit
`ec7c4da3e0774264275377cd6b7687474bad577a`, licensed under the Apache License, Version 2.0
(see the header of each file, and https://www.apache.org/licenses/LICENSE-2.0).

Vendored (not added as a Gradle dependency) because the maintained Compose wrapper for this algorithm
(`com.materialkolor:material-kolor`) requires Kotlin 2.x, and this project is on Kotlin 1.9.24.

Two files (`dynamiccolor/ColorSpec2021.java`, `dynamiccolor/DynamicColor.java`) have had Google-internal
Error Prone static-analysis annotations (`@Var`, `@CanIgnoreReturnValue`) removed — these have no
runtime or compiled behavior, they only silence a linter this project doesn't run — to avoid adding
`com.google.errorprone:error_prone_annotations` as a dependency for two unused annotations. No other
modification was made to any vendored file.

Only the subset needed to generate a `SchemeFidelity` from a single seed color is vendored — the
image-quantization (wallpaper color extraction) and icon-harmonization code from the upstream project
was not needed and is not included.
