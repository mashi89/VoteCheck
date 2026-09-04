# Contributing

## Branching

One topic per branch, branched from an up-to-date `master`. Nothing is committed to
`master` directly.

### Issues first

**Every change has an issue.** If one does not already exist, create it before branching —
that is part of the work, not a precondition someone else supplies. The issue holds the *why*:
what is wrong, or what should exist, and how anyone would tell it was done. The branch and
pull request carry only the *what*, and the issue number ties the two together.

This is what makes the branch names below worth having. A list reading `bugfix/31-…`,
`feature/42-…` can be traced back to a stated reason; one reading `bugfix/that-thing` cannot.

The single carve-out is about ordering, not exemption: a `hotfix/` while production is down
does not wait on paperwork. File the issue as soon as the fix is out — an urgent change is
precisely the one whose reasoning someone will want later.

### Issue status is never stale

An issue's state should never need interpreting. Open means still wanted and not yet done.

- **Link the pull request to the issue** with `Closes #33` in its description, so merging
  closes the issue and nobody has to remember to. Use `Refs #33` where the pull request only
  advances the issue rather than finishing it — a half-fix that auto-closes is worse than no
  link at all, because the list then lies.
- **An issue that is no longer wanted gets closed as *not planned*,** with a sentence saying
  why. Leaving it open costs everyone who scans the list a fresh re-read of a decision that
  was already made.
- **Something resolved outside a pull request still gets closed** — fixed upstream, or made
  moot by another change — with a note saying what happened.
- **If the work turns out different from what the issue describes, edit the issue.** The
  branch should not quietly drift away from its own stated reason; whichever of the two is
  now wrong is the one to correct.
- **Reopen rather than file a duplicate** when a fix did not hold. The history of a thing that
  broke twice is worth more than two half-histories.

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

1. Open an issue, or find the existing one.
2. Branch from current `master`.
3. Keep to one topic. Finding a second one is a reason to branch again, not to widen this one.
4. Open a pull request, linking the issue (`Closes #33`). CI must pass before merge.
5. Delete the branch after merge, locally and on the remote.
6. **A merged branch is finished.** Never add commits on top of merged history — start a new
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
