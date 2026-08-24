# Scoop Plugin for Flow Launcher

This plugin integrates [Scoop](https://scoop.sh/), the Windows command-line installer, directly into [Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher), allowing for seamless app management.

## Prerequisites

*   **Scoop Installation:** Ensure Scoop is installed on your system.  If not, install it following the instructions on the [Scoop website](https://scoop.sh/).

The plugin settings contain separate optional paths for the user and global Scoop roots. `Scoop Home (user root)` is the user-level root from issue #1; `Scoop Home (global root)` is only needed when the global root is not discoverable through `SCOOP_GLOBAL`, `global_path`, or the default `%ProgramData%\scoop` root. The default global application directory is `%ProgramData%\scoop\apps`. The two settings are independent and do not override each other.

## install

    pm install Scoop by xavialyra
## Features

This plugin provides the following functionality:

*   **`scoop list` (Installed Apps):**
    *   **Action:** Lists all applications installed through Scoop, including user and global installations. Global installations are marked `global`; user installations use the default display.
    *   **Click:** Opens the application (if applicable).
    *   **`Ctrl + Enter`:** Opens the application's installation directory in File Explorer.
    *   **Context Menu:**
        *   **Check Version:** Displays the current version of the application.
        *   **Open Homepage:** Opens the application's homepage in your browser.
        *   **Update:** Updates the application to the latest version in its detected scope. Global updates are shown only when Flow Launcher runs as administrator.
        *   **Uninstall:** Uninstalls the application in its detected scope. Global uninstalls are shown only when Flow Launcher runs as administrator.
        *   **Reset:** Resets user-scope applications. Reset is not shown for global applications because Scoop has no global reset switch.


*   **`scoop search` (Search Apps):**
    *   **Action:** Searches installed bucket manifests by app name, binaries, aliases, and shortcuts.
    *   **Click:** Opens the application's homepage in your browser.
    *   **`Ctrl + Enter`:** Opens the application's configuration file (if available) in File Explorer.
    *   **Context Menu:**
        *   **Intro:** Displays a brief description of the application.
        *   **Open Homepage:** Opens the application's homepage in your browser.
        *   **Install:** Installs the application into the user Scoop root and shows the detected destination path.
        *   **Install (global):** Available only when Flow Launcher runs as administrator; installs into the global Scoop apps directory.

*   **`scoop update` (Available Updates):**
    *   Checks installed applications for available updates and lists them.
    *   Selecting an application updates it in its detected user or global installation scope. Global update actions are available only when Flow Launcher runs as administrator.
    *   **Update Scoop and buckets:** Runs `scoop update`.
    *   **Update all user applications:** Runs `scoop update --all`.
    *   **Update all global applications:** Runs `scoop update --all --global` when a global Scoop installation is detected and Flow Launcher runs as administrator.
    *   Entering `*` after `update` immediately opens the available batch actions without waiting for the status scan.

![PixPin_2025-03-23_18-24-14](https://github.com/user-attachments/assets/d3583a01-03a3-4a38-afe4-3d55ba142cf0)
