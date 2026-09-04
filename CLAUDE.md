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
- **Open an issue first** whenever there is anything worth describing, then lead the branch
  description with its number, following the category prefix:
  `bugfix/31-sitemap-advertises-http`. Skip the issue only when the change is smaller than the
  issue would be, or when production is down and a `hotfix/` cannot wait — file it afterwards
  in that case.
- **One topic per branch.** Finding a second topic means opening a second branch, not widening
  this one.
- **A merged branch is finished.** Never add commits to merged history — branch again from
  `master`.

## Deployment

`deploy/README.md` is the runbook for edustajavahti.fi. Two things there that are not
guessable and were both found the hard way:

- `ufw` does not protect published Docker ports — Docker's iptables rules are evaluated
  first. `deploy/cloudflare-firewall.sh` filters in `DOCKER-USER` for that reason, and the
  only meaningful check is a request from another machine.
- The origin IP must never appear in an unproxied DNS record, not even briefly.
