# Github Launcher

[![.NET 9](https://img.shields.io/badge/.NET-9-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/SirDiabo/GithubLauncher)](https://github.com/SirDiabo/GithubLauncher/blob/main/LICENSE)

>## Important
>### Migration from N64RecompLauncher to GithubLauncher
>
>This software was previously knows as the "N64Recomp Launcher", but this had to be changed due to continued requests by the N64Recomp Tool Developers. If you are interested in a version of the launcher that is primarily for N64 Recompiled Games, then check out [this version](https://github.com/SirDiabo/GithubLauncher/releases/tag/v1.70).
>You can then go into the `update_check.json` and change the year in `LastCheckTime` to something like `9999` to disable self-updates.
>
>If you have an old version, and wish to update to the newer ones, you will have to update manually by dragging the new files into your launcher folder. You can then delete all files with "N64Recomp" in their names.
>Your settings will remain compatible.

![Github Launcher Screenshot](Assets/LauncherScreenshot.png)
A modern, user-friendly launcher application for managing and running GitHub-hosted applications. This tool streamlines the process of downloading, installing, and launching your favorite projects.

## Features

- **Automated Updates**: Seamlessly download and install the latest releases from GitHub
- **Version Management**: Stay up-to-date with automatic version checking and updates
- **App Management**: Easy-to-use interface for launching GitHub-hosted apps
- **Smart Integration**: Direct integration with GitHub releases for smooth updates

## Getting Started

### Prerequisites

- .NET 9 Runtime (get it [here](https://dotnet.microsoft.com/en-us/))
- Internet connection for updates and downloads

### Installation

1. Download the latest release from the [Releases](https://github.com/SirDiabo/GithubLauncher/releases) page
2. Extract the downloaded archive to your preferred location
3. Run the executable.

#### Nix / NixOS

GithubLauncher can be run directly with:

```sh
nix run github:SirDiabo/GithubLauncher
```

Or add GithubLauncher as a flake input:

```nix
inputs.github-launcher = {
  url = "github:SirDiabo/GithubLauncher";
  inputs.nixpkgs.follows = "nixpkgs";
};
```

Then install it from the flake output:

```nix
home.packages = [
  inputs.github-launcher.packages.${pkgs.system}.githublauncher
];
```

## Usage

1. Launch the application
2. The launcher will automatically check for updates on startup
3. Browse your app library through the interface
4. Select an app and click "Download/Launch" to use it

## Configuration

### GitHub API Token
To avoid hitting GitHub's API rate limits, you can provide a personal access token.
Create a token with no special permissions needed and set it in the launcher settings.
You can create a token at ```GitHub Settings -> Developer settings > Personal access tokens > Tokens (classic) > Generate new token```
You don't need to give it any special permissions. Then paste that Token into your Settings field. Do not share your Token!

### apps.json Structure

The launcher uses an `apps.json` file to manage the available apps. Every entry is user-managed and editable; there are no built-in stable, experimental, or custom categories.

#### App Entry Properties

Each app entry requires the following properties:

- **`name`** - The display name of the app as it appears in the launcher
- **`repository`** - The GitHub repository in the format `username/repository`
- **`folderName`** - The folder name where the app will be downloaded and installed
- **`appIconUrl`** - URL of the app's icon image. If null, a default icon will be used.

#### Example Configuration

```json
{
    "apps": [
        {
            "name": "Example App",
            "repository": "username/example-app-repo",
            "folderName": "ExampleApp",
            "appIconUrl": null
        },
        {
            "name": "Another App",
            "repository": "anotheruser/another-app-repo",
            "folderName": "AnotherApp",
            "appIconUrl": "link/to/an/image.png"
        }
    ]
}
```

## Support

If you encounter any issues or have questions:
- [Open an issue](https://github.com/SirDiabo/GithubLauncher/issues)
- Check existing issues for solutions
