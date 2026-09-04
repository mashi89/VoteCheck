# VoteCheck / Edustajavahti

## Branching — read before the first commit

`CONTRIBUTING.md` holds the convention. The parts that are easy to get wrong:

- **Never commit to `master`.** Never push to the session's generated `claude/<words>`
  branch either — it is not a category and it names nothing. Create the real branch before
  the first commit:

  ```
  git checkout -B <category>/<issue-number-if-any>-<short-description> origin/master
  ```

- **Categories:** `feature/`, `bugfix/`, `hotfix/`, `docs/`, `chore/`, `refactor/`. `bugfix/`
  is the name for a wrong-behaviour fix; the short form is not used.
- **Every change has an issue, and creating it is your job.** Before the first commit, find
  the existing issue or open a new one on GitHub describing what is wrong or what should
  exist. Do not start work on the assumption that someone else will file it. Its number then
  leads the branch description, after the category prefix:
  `bugfix/31-sitemap-advertises-http`. The only carve-out is ordering — a `hotfix/` while
  production is down ships first and gets its issue immediately afterwards.
- **One topic per branch.** Finding a second topic means opening a second branch, not widening
  this one.
- **A merged branch is finished.** Never add commits to merged history — branch again from
  `master`.
- **Keep the issue's status true.** Link the pull request with `Closes #33` so the merge closes
  it, or `Refs #33` if the change only advances it. Close abandoned issues as *not planned*
  with a reason, close anything resolved elsewhere with a note, and edit the issue if the work
  turns out to be different from what it describes.

## Deployment

`deploy/README.md` is the runbook for edustajavahti.fi. Two things there that are not
guessable and were both found the hard way:

- `ufw` does not protect published Docker ports — Docker's iptables rules are evaluated
  first. `deploy/cloudflare-firewall.sh` filters in `DOCKER-USER` for that reason, and the
  only meaningful check is a request from another machine.
- The origin IP must never appear in an unproxied DNS record, not even briefly.
