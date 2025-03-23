# Scoop Plugin for Flow Launcher

This plugin integrates [Scoop](https://scoop.sh/), the Windows command-line installer, directly into [Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher), allowing for seamless app management.

## Prerequisites

*   **Scoop Installation:** Ensure Scoop is installed on your system.  If not, install it following the instructions on the [Scoop website](https://scoop.sh/).

## install

    pm install Scoop by xavialyra
## Features

This plugin provides the following functionality:

*   **`scoop list` (Installed Apps):**
    *   **Action:** Lists all applications installed through Scoop.
    *   **Click:** Opens the application (if applicable).
    *   **`Ctrl + Enter`:** Opens the application's installation directory in File Explorer.
    *   **Context Menu:**
        *   **Check Version:** Displays the current version of the application.
        *   **Open Homepage:** Opens the application's homepage in your browser.
        *   **Update:** Updates the application to the latest version.
        *   **Uninstall:** Uninstalls the application.
        *   **Reset:** Resets the application to its default state.


*   **`scoop search` (Search Apps):**
    *   **Action:** Searches for applications within your installed Scoop buckets.
    *   **Click:** Opens the application's homepage in your browser.
    *   **`Ctrl + Enter`:** Opens the application's configuration file (if available) in File Explorer.
    *   **Context Menu:**
        *   **Intro:** Displays a brief description of the application.
        *   **Open Homepage:** Opens the application's homepage in your browser.
        *   **Install:** Installs the application using Scoop.

