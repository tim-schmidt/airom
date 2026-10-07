# Contributing to AIrom

Thank you for helping. AIrom is a faithful port of Umoria 5.6, so the bar for a
change is that the game still plays the way the 1989 original does.

## Reporting a bug

Open an issue using the bug report template. The most useful reports say:

- your operating system and the terminal the game was running in, with its size
- what you did, step by step, and what happened compared with what you expected
- whether a real Umoria 5.6 does the same thing, if you know

A savefile that shows the problem is the best evidence there is. Attach it to
the issue if you can.

## Sending a change

Fork the repository, make your change on a branch in your fork, and open a pull
request against `main`. Nobody pushes to `main` directly, the maintainer
included.

Before you open the pull request:

```
dotnet publish src/Airom -c Release
dotnet test tests/Airom.Tests
```

Both must pass. Warnings are treated as errors.

Keep each pull request to one change, and say in its description what you
checked. A change to how the game plays should say which part of the original
it follows, by file and function.

## The oracle

`tools/oracle/` builds the original C and compares its output with the port's,
line for line. It is the closest thing the port has to a specification;
`tools/oracle/README.md` explains it in full. It runs on Windows only and needs
the Umoria 5.6 sources, which are not part of this repository.

If you can run it, run the full sweep before opening a pull request that
changes how the game behaves:

```
bash tools/oracle/regress.sh
```

If you can't, say so in the pull request, and the maintainer will run it.

## Faithful quirks

Places marked `FAITHFUL QUIRK` reproduce oddities of the original on purpose.
They are a backlog to be settled together in a later pass, so please don't
"fix" one on its own. If you think one is a real bug, open an issue about it
instead.

## License

AIrom is GPL-3.0-or-later, as Umoria is. By sending a pull request you agree
that your contribution is licensed the same way.
