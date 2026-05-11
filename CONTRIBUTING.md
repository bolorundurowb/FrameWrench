# Contributing

Thank you for considering a contribution to FrameWrench.

1. Fork the repository and create a feature branch.
2. Ensure `dotnet test` passes on all targets before opening a PR.
3. Add tests for any new protocol behaviour.
4. Follow the existing XML-doc comment style.
5. **CI and Codecov:** pushes and PRs to `master` run `.github/workflows/ci.yml` (build, test, coverage). Maintainers should add a **`CODECOV_TOKEN`** repository secret from [Codecov](https://codecov.io) so coverage uploads succeed; forks may rely on Codecov's tokenless rules for public repositories when the upstream org allows it.
