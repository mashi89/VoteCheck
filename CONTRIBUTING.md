# Contributing

## Branching

One topic per branch, branched from an up-to-date `master`. Nothing is committed to
`master` directly.

### Categories

| Prefix | For | Example |
|---|---|---|
| `feature/` | A new capability | `feature/mp-topic-search` |
| `bugfix/` | Behaviour that is wrong | `bugfix/dash-healthcheck` |
| `hotfix/` | An urgent repair to what is deployed right now | `hotfix/origin-firewall-bypass` |
| `docs/` | Documentation and roadmap only | `docs/selkokieli-roadmap` |
| `chore/` | Dependencies, CI, tooling — no behaviour change | `chore/dotnet-10` |
| `refactor/` | Structure changes, behaviour preserved | `refactor/split-queries` |

`hotfix/` is separate from `bugfix/` because this repository has a live deployment: it branches
from `master`, carries the smallest change that repairs production, and merges as soon as CI is
green. A `bugfix/` can wait for an unhurried review; a `hotfix/` is the one that cannot.

### Names

`category/short-kebab-case`. Lowercase, hyphens, no spaces or underscores. Name the change,
not the file it lives in — `bugfix/sitemap-advertises-http` rather than `bugfix/program-cs`.

**Where an issue exists, its number leads the description**, immediately after the category
prefix: `bugfix/31-sitemap-advertises-http`, `feature/42-mp-topic-search`. The prefix stays in
front so branches still group by category; a number before it would order the list by issue
age instead, which is the one ordering nobody reads it in. Work with no issue behind it needs
no number — this is required only when there is something to cite.

### Lifecycle

1. Branch from current `master`.
2. Keep to one topic. Finding a second one is a reason to branch again, not to widen this one.
3. Open a pull request. CI must pass before merge.
4. Delete the branch after merge, locally and on the remote.
5. **A merged branch is finished.** Never add commits on top of merged history — start a new
   branch from `master`. The merged pull request cannot track new work, and stacking on it
   produces a branch whose diff against `master` no longer describes what changed.

Branches are short-lived. A branch that cannot be merged this week is usually two topics.

### Claude Code sessions

A session started from the Claude Code UI is placed on a generated branch named
`claude/<words>`. Work does not go there. That name is not a category and describes nothing,
and six months later it is indistinguishable from every other session in the branch list.

Create a branch under the convention above and use it from the first commit:

```
git checkout -B feature/42-mp-topic-search origin/master
```

The generated branch is then simply never used, and can be deleted.
