# <img src="/src/icon.png" height="30px"> BuildMonitor

[![Build status](https://github.com/SimonCropp/BuildMonitor/actions/workflows/test.yml/badge.svg?branch=main)](https://github.com/SimonCropp/BuildMonitor/actions/workflows/test.yml) [![NuGet Status](https://img.shields.io/nuget/v/BuildMonitor.svg?label=BuildMonitor)](https://www.nuget.org/packages/BuildMonitor/)

A build/CI monitor that runs in the system tray on Windows, macOS and Linux. It polls the CI services a developer cares about, shows one row per pipeline with a progress bar and countdown for running builds, links straight to the build, the branch and the pull request, can retry or cancel a build, and copies the log of a failed one. A local [MCP](/docs/mcp.md) server lets an AI assistant read and act on the same list.

<img src="/src/BuildMonitor.Windows.Tests/MonitorFormTests.Searched.verified.png">

**See [Milestones](../../milestones?state=closed) for release notes.**

toc


## Install

```
dotnet tool install --global BuildMonitor --prerelease
```

Run `buildmonitor` to start the tray app. One install works on every operating system: the package carries a head for each platform and the launcher starts the right one.

To update, use [Update](/docs/options.md#update) in the tray menu, which restarts BuildMonitor on the new version. To update from the command line on Windows, first quit the tray with `buildmonitor quit` and close any AI assistant using the MCP server, since both hold the installed files:

```
dotnet tool update --global BuildMonitor --prerelease
```

An assistant stays on the old version of the MCP server until it [connects it again](/docs/mcp.md#updating).


## Supported services

include: providers

MyGet is not supported: it exposes no build API, only a status badge, a trigger hook and webhooks.


## Documentation

include: doc-index


## Icons

Icons are from [Lucide](https://lucide.dev/) (ISC), bundled through [IconifyBundle](https://github.com/Papyrine/IconifyBundle). Provider logos are from [Simple Icons](https://simpleicons.org/) (CC0), in each brand's own colour. GitHub's is the octocat badged with a play mark, since a row draws the repository's host beside the service that built it and the two cannot be one picture.


## Fonts

The heads that rasterise their own text draw with [JetBrains Mono](https://www.jetbrains.com/lp/mono/) (OFL), with [Noto Emoji](https://fonts.google.com/noto/specimen/Noto+Emoji) (OFL) merged over it for the 🤖 a bot's row carries, which a programming face has no glyph for. Both are embedded, and Noto Emoji is cut down to the marks actually drawn; see `EmbeddedFont` for the command that cuts it. Each licence sits beside its font under `src/BuildMonitor.Core/Assets`.
