# <img src="/src/icon.png" height="30px"> BuildMonitor

[![Build status](https://github.com/SimonCropp/BuildMonitor/actions/workflows/test.yml/badge.svg?branch=main)](https://github.com/SimonCropp/BuildMonitor/actions/workflows/test.yml) [![NuGet Status](https://img.shields.io/nuget/v/BuildMonitor.svg?label=BuildMonitor)](https://www.nuget.org/packages/BuildMonitor/)

A build/CI monitor that runs in the system tray on Windows, macOS and Linux. It polls the CI services a developer cares about, shows one row per pipeline with a progress bar and countdown for running builds, links straight to the build, the branch and the pull request, and can retry or cancel a build. A local [MCP](/docs/mcp.md) server lets an AI assistant read and act on the same list.

**See [Milestones](../../milestones?state=closed) for release notes.**

toc


## Install

```
dotnet tool install -g BuildMonitor
```

Run `buildmonitor` to start the tray app. One install works on every operating system: the package carries a head for each platform and the launcher starts the right one.


## Supported services

include: providers

MyGet is not supported: it exposes no build API, only a status badge, a trigger hook and webhooks.


## Documentation

include: doc-index


## Icons

Icons are from [Lucide](https://lucide.dev/) (ISC), bundled through [IconifyBundle](https://github.com/Papyrine/IconifyBundle).
