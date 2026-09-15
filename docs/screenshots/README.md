# Screenshots

Illustrations for pull requests and issues, so a reviewer can see a change without
building it. They are snapshots of one moment, not living documentation — assume any
image here is as old as the commit that added it, and trust the code over the picture.

Captured from `tools/votecheck-sample.db` with headless Chrome, so they contain only
data that is already public and already in this repository:

```
dotnet run --project VoteCheckWeb                     # with VoteCheck__DbPath set to the sample
google-chrome-stable --headless --window-size=1100,1500 \
  --screenshot=out.png http://127.0.0.1:5177/vote/2009-127-78
```

Add `--force-dark-mode` for the dark variant.
