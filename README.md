# lf-windows User Manual

[中文文档](README_zh.md)

## 1. Introduction
`lf-windows` is a modern terminal file manager designed for Windows 11, replicating the core experience of `lf` and deeply optimized for the Windows desktop environment. It adopts keyboard-first operation logic combined with Vim-style keybindings, aiming to provide an efficient and immersive file management experience.
<img width="748" height="523" alt="lf-windows0" src="https://github.com/user-attachments/assets/c6258bc5-18a4-4d80-8f78-9af91d13e885" />
<img width="748" height="523" alt="lf-windows1" src="https://github.com/user-attachments/assets/51e1dd1d-c1df-461a-b0e6-4cf9cd2f2009" />
## 2. Core Interface
The interface uses a classic three-column layout (Miller Columns):
- **Left Column (Parent)**: Displays the contents of the parent directory.
- **Middle Column (Current)**: Displays the contents of the current working directory with the selected item highlighted.
- **Right Column (Preview)**: Displays the preview of the selected file (supports text, images, videos, PDF, Office documents, archives, etc.).

Bottom section includes:
- **Status Bar**: Displays current file info, selection count, filter status, etc.
- **Command Line**: Used for command input, search mode, and filter mode interactions.

## 3. Basic Operations

### 3.1 Navigation
| Action | Key | Description |
| :--- | :--- | :--- |
| Move Up | `k` or `Up` | Move cursor up |
| Move Down | `j` or `Down` | Move cursor down |
| Enter Dir/Open File | `l` or `Right` | Enter selected directory or open file |
| Go to Parent Dir | `h` or `Left` | Return to parent directory |
| Top | `gg` | Jump to top of list |
| Bottom | `G` | Jump to bottom of list |
| Page Up Half | `Ctrl+U` | Scroll up half page |
| Page Down Half | `Ctrl+D` | Scroll down half page |
| Page Up Full | `Ctrl+B` | Scroll up one page |
| Page Down Full | `Ctrl+F` | Scroll down one page |

### 3.2 File Operations
| Action | Key | Description |
| :--- | :--- | :--- |
| Copy (Yank) | `y` | Add selected files to clipboard (Copy mode) |
| Cut | `x` | Add selected files to clipboard (Move mode) |
| Paste | `p` | Paste files from clipboard to current directory |
| Delete | `Delete` or `D` | Move selected files to Recycle Bin |
| Rename | `r` | Rename current file |
| New File | `n` | Create new file in current directory |
| New Folder | `N` | Create new folder in current directory |
| Create Shortcut | `Ctrl+L` | Create shortcut for files in clipboard |
| Clear Clipboard | `c` | Clear current clipboard state |
| View History | `P` | Open Clipboard History panel (Yank History) |

### 3.3 Selection Operations
| Action | Key | Description |
| :--- | :--- | :--- |
| Toggle Selection | `Space` | Select/Deselect current item and move down |
| Visual Mode | `V` | Enter/Exit Visual Mode |
| Invert Selection | `v` | Invert selection in current directory |
| Unselect All | `u` | Unselect all items |

## 4. Search & Filter (Core Features)

`lf-windows` provides powerful search and filter capabilities, supporting four matching modes. All modes are **case-insensitive** by default and support **Chinese Pinyin Initials** matching.

#### Matching Modes

| Mode | Keyword | Description | Example |
| :--- | :--- | :--- | :--- |
| **Fuzzy** | `fuzzy` | **Fuzzy Match** (Default). Characters just need to appear in order. Good for quick lookup. | Input `doc` matches `Documents`, `d_o_c.txt` |
| **Text** | `text` | **Text Contains**. Filename must contain the exact substring. | Input `log` matches `syslog.txt`, but not `l_o_g.txt` |
| **Glob** | `glob` | **Wildcard Full Match**. Uses `*` and `?`. Note this is **Anchored** (Full Match). | `*.txt` (all txt files), `data_??.dat` |
| **Regex** | `regex` | **Regular Expression**. Uses .NET Regex engine for complex matching. | `^\d+` (starts with digit), `(jpg|png)$` (image suffix) |

> **Tip**: Pinyin matching example: In Fuzzy mode, input `wjj` matches `文件夹 (WenJianJia)`.

#### Glob Guide
Glob mode uses standard Shell wildcard syntax, but note it is a **Full Match**. This means your pattern must match the **entire** filename, not just a part of it.

| Symbol | Meaning | Example |
| :--- | :--- | :--- |
| `*` | Matches any number of characters (including zero) | `*.txt` matches all txt files<br>`*report*` matches files containing 'report' |
| `?` | Matches any **single** character | `img_??.jpg` matches `img_01.jpg`, but not `img_1.jpg` or `img_001.jpg` |

**Common Mistakes**:
- Input `xml` **fails** to match `config.xml`. Must use `*.xml` or `*xml`.
- Input `test` **fails** to match `my_test_file`. Must use `*test*`.

#### Regex Reference
In Regex mode, common symbols supported (based on .NET Regex engine):

| Symbol | Meaning | Example |
| :--- | :--- | :--- |
| `.` | Matches any char except newline | `a.c` matches `abc`, `a+c` |
| `*` | Matches previous char 0 or more times | `ab*c` matches `ac`, `abc`, `abbc` |
| `+` | Matches previous char 1 or more times | `ab+c` matches `abc`, `abbc` (not `ac`) |
| `?` | Matches previous char 0 or 1 time | `colou?r` matches `color`, `colour` |
| `^` | Matches start of string | `^Log` matches files starting with `Log` |
| `$` | Matches end of string | `.txt$` matches files ending with `.txt` |
| `[]` | Character set | `[abc]` matches `a`, `b`, or `c` |
| `[^]` | Negated character set | `[^0-9]` matches non-digits |
| `|` | OR | `jpg|png` matches `jpg` or `png` |
| `()` | Grouping | `(ab)+` matches `ab`, `abab` |
| `\d` | Matches digit | `file_\d+` matches `file_123` |
| `\w` | Matches word char | `\w+` |
| `\s` | Matches whitespace | |

### 4.4 Chinese Pinyin Support
`lf-windows` provides deep Pinyin support for Chinese filenames, allowing you to filter Chinese files using English characters.

#### 1. Fuzzy Mode (Mixed Match)
In Fuzzy mode, matching is done **character by character**. Each input char can match either the original char or its Pinyin initial.
- **Feature**: Supports mixed Chinese/English input.
- **Example**: Filename `我的文档.txt`
  - Input `wdwd` -> Match (W-D-W-D)
  - Input `我dwd` -> Match (我-D-W-D)
  - Input `w的wd` -> Match (W-的-W-D)

#### 2. Text / Glob / Regex Mode (Double Match)
In these modes, the system checks both the **original filename** and the **Pinyin initials string**. A match on either is considered a success.
- **Logic**: File `我的文档.txt` is treated as two targets:
  1. `我的文档.txt` (Original)
  2. `wdwd.txt` (Pinyin Initials)
  
- **Example**:
  - **Text**: Input `wd` matches `我的文档` (because `wdwd` contains `wd`).
  - **Glob**: Input `w*.txt` matches `我的文档.txt` (because `wdwd.txt` fits `w*.txt`).
  - **Regex**: Input `^w\w+d` matches `我的文档` (because `wdwd` fits regex).

> **Note**: Pinyin matching only supports **Initials**, not full Pinyin. E.g., `我的` corresponds to `wd`, not `wode`.

### 4.1 Filter Mode
Filter mode hides non-matching files in real-time.
- **Enter Filter**: Press `i`.
- **Default Mode**: Fuzzy.
- **Switch Mode**: Input `:filter <mode> <content>` in command line.
  - Ex: `:filter regex ^[0-9]+`
  - Ex: `:filter glob *.txt`

### 4.2 Search Mode
Search mode highlights matches and supports jumping, without hiding files.
- **Search Down**: Press `/`.
- **Search Up**: Press `?` (Shift+/).
- **Next Match**: `=` (Note: unlike standard lf `n`).
- **Prev Match**: `-` (Note: unlike standard lf `N`).

### 4.3 Find Mode
For quick jumping within the current screen.
- **Find Char**: Press `f` then a char to jump to next occurrence.
- **Find Back**: Press `F` then a char to jump to prev occurrence.
- **Repeat**: `.` (Next), `,` (Prev).

## 5. Panels & Tools

### 5.1 Command Panel
- **Shortcut**: `zz`
- **Function**: Opens a grid panel with common commands.
- **Customization**: Commands can be customized in settings.

### 5.2 Bookmarks & Jump
- **Save Bookmark**: Press `m`, then a char (e.g., `a`).
- **Jump Bookmark**: Press `;`, then the char (e.g., `a`).
- **Built-in**: Some bookmarks may be preset.

### 5.3 Workspace Panel
- **Shortcut**: `wo` (Toggle Workspace)
- **Function**: Workspaces are like "Favorites Groups" or "Project Contexts". Panel has Left (Workspace List) and Right (Shortcut List).

- **Basic**:
  - `Tab`: Switch focus between lists.
  - `Esc`: Close panel.

- **Workspace Management (Left Focus)**:
  - `ws`: **New** Workspace.
  - `wr`: **Rename** Workspace.
  - `wd`: **Delete** Workspace.
  - `j` / `k`: Select Workspace.
  - `1`-`9`: Quick jump.
  - `l`: Enter Right list.

- **Shortcut Management (Right Focus)**:
  - `Enter`: Open file/dir.
  - `c`: **Delete** shortcut (Clear).
  - `j` / `k`: Select shortcut.
  - `1`-`9`: Quick open.
  - `h`: Return to Left list.

- **Add to Workspace**:
  1. Select files in main list.
  2. Input `wl` (Workspace Link).
  3. Select target workspace and `Enter`.

### 5.4 Clipboard & History

`lf-windows` has a powerful clipboard system with **Active Clipboard** and **History**.

#### Active Clipboard
Files currently ready to paste.
- **Yank**: `y` (Copy).
- **Cut**: `x` (Move).
- **Paste**: `p`.
- **Clear**: `c` (Clear state).

#### Yank History
Every copy/cut is saved to history.
- **Open Panel**: `P` (Shift+p).
- **Operations**:
  - `j` / `k`: Select item.
  - `Enter` / `Space`: **Confirm & Paste**. Reactivates item as Active Clipboard and pastes immediately.
  - `c`: **Delete** item.
  - `1`-`9`: Quick select & paste.
  - `Esc`: Close.

### 5.5 View & Appearance
Switch views via Command Panel (`zz`) or commands.

- **Float Mode**
  - **Command**: `:float`
  - **Description**: Hides window title bar for immersive experience. Great for Tiling Window Managers.

- **Compact Mode**
  - **Command**: `:compact`
  - **Description**: Reduces row height/padding. Shows more files on small screens.

- **Icons**
  - **Command**: `:icon`
  - **Description**: Toggle file icons.

- **Preview Toggle**
  - **Command**: `:preview`
  - **Description**: Toggle right preview column.
    - **On**: Rich previews.
    - **Off**: Two-column layout. Reduces memory usage and improves performance in huge directories.

### 5.6 File Watcher & Sync
`lf-windows` uses a separate background process `lf-watcher.exe` for real-time file system sync.

#### 1. Independent Process Architecture
- **Main (`lf-windows.exe`)**: UI, interaction, preview.
- **Watcher (`lf-watcher.exe`)**: Monitors `Create`, `Delete`, `Rename`, `Change` events.

#### 2. Sync with Explorer
- **Real-time**: Changes in Windows Explorer reflect immediately in `lf-windows`.
- **Bi-directional**: Changes in `lf-windows` reflect in Explorer.
- **Anti-Freeze**: Heavy file operations won't freeze the UI thanks to the separate process.

#### 3. Troubleshooting
- `lf-watcher.exe` must be in the same directory as the main executable.
- If list doesn't refresh, check if `lf-watcher.exe` is running.

## 6. Keybindings

Default keybindings (customizable in `config.yaml`).

### 6.1 Navigation
| Key | Function | Note |
| :--- | :--- | :--- |
| `k` / `Up` | Move Up | |
| `j` / `Down` | Move Down | |
| `h` / `Left` | Parent Dir | |
| `l` / `Right` | Enter Dir / Open | |
| `gg` | Top | |
| `G` | Bottom | |
| `Ctrl+U` | Up Half Page | |
| `Ctrl+D` | Down Half Page | |
| `Ctrl+B` | Up Full Page | |
| `Ctrl+F` | Down Full Page | |
| `gh` | Go Home | |

### 6.2 File Operations
| Key | Function | Note |
| :--- | :--- | :--- |
| `o` | Open File | System Default |
| `e` | Open in Explorer | |
| `n` | New File | |
| `N` | New Folder | |
| `r` | Rename | |
| `Delete` / `D` | Delete | Recycle Bin |
| `F5` | Refresh | |

> **Tip**: Open Terminal is not bound by default. Use Command Panel (`zz`) -> `open-terminal` or bind it in config (e.g., `Normal.OpenTerminal: w`).

### 6.3 Clipboard
| Key | Function | Note |
| :--- | :--- | :--- |
| `y` | Yank | Copy |
| `x` | Cut | Move |
| `p` | Paste | |
| `c` | Clear | Clear Clipboard |
| `Ctrl+L` | Create Shortcut | |
| `P` | History | Yank History |

### 6.4 Selection
| Key | Function | Note |
| :--- | :--- | :--- |
| `Space` | Toggle Selection | |
| `v` | Invert Selection | |
| `u` | Unselect All | |
| `V` | Visual Mode | |
| `o` | Exchange | Swap cursor/anchor |

### 6.5 Search & Find
| Key | Function | Note |
| :--- | :--- | :--- |
| `/` | Search Down | |
| `?` | Search Up | |
| `=` | Next Match | |
| `-` | Prev Match | |
| `f` | Find Char | |
| `F` | Find Char Back | |
| `.` | Repeat Next | |
| `,` | Repeat Prev | |
| `i` | Filter Mode | |

### 6.6 View & Sort
| Key | Function | Note |
| :--- | :--- | :--- |
| `sn` | Sort Natural | |
| `ss` | Sort Size | |
| `st` | Sort Time (Mod) | |
| `sa` | Sort Access Time | |
| `sb` | Sort Birth Time | |
| `sc` | Sort Change Time | |
| `se` | Sort Extension | |
| `zr` | Toggle Reverse | |
| `zd` | Toggle Dir First | |
| `zh` | Toggle Hidden | |
| `z.` | Toggle Dotfiles | |
| `zs` | Info: Size | |
| `zt` | Info: Time | |
| `za` | Info: Size & Time | |
| `zp` | Info: Perms | |
| `zn` | Info: None | |
| `Alt+1` | Small Window | |
| `Alt+2` | Large Window | |
| `F11` | Fullscreen | |

### 6.7 Panels
| Key | Function | Note |
| :--- | :--- | :--- |
| `zz` | Command Panel | |
| `m` | Mark | Save Bookmark |
| `;` | Jump | Jump Bookmark |
| `wo` | Workspace | Toggle Panel |
| `q` | Quit | Quit App/Panel |

### 6.8 Workspace Mode Keys
When Workspace Panel (`wo`) is open:
- **General**: `Tab` (Switch focus), `Esc` (Close).
- **Left (Workspaces)**:
  - `ws`: New.
  - `wr`: Rename.
  - `wd`: Delete.
  - `l`: Enter Right.
- **Right (Shortcuts)**:
  - `Enter`: Open.
  - `c`: Delete.
  - `h`: Return Left.

### 6.9 History Mode Keys
When History Panel (`P`) is open:
- `j` / `k`: Select.
- `Enter` / `Space`: **Confirm & Paste**.
- `c`: **Delete**.
- `Esc`: Close.

### 6.10 Preview & Popups

#### Preview Pane
| Key | Function | Note |
| :--- | :--- | :--- |
| `Up` / `Down` | Scroll Vertical | Scroll Preview |
| `Left` / `Right` | Scroll Horizontal | Scroll Preview |
| `t` | Popup Preview | Open in new window |

#### Popup Window
When using `t`:
| Key | Function | Note |
| :--- | :--- | :--- |
| `j` / `Down` | Scroll Down | |
| `k` / `Up` | Scroll Up | |
| `h` | Rotate Left | Images (-90°) |
| `l` | Rotate Right | Images (+90°) |
| `Left` / `Right` | Page Flip | PDF |
| `Esc` | Close | |

#### Dialogs
| Key | Function | Note |
| :--- | :--- | :--- |
| `y` | Yes | Confirm |
| `n` | No | Cancel |
| `Esc` | Cancel | |

## 7. Configuration
Config file located at `%APPDATA%\lf-windows\` (`C:\Users\User\AppData\Roaming\lf-windows\`).
- **config.yaml**: Contains all settings including appearance and keybindings.

You can modify `keyBindings` in `config.yaml` to restore standard `lf` habits (e.g., mapping `n` back to search next).

## 8. Credits
`lf-windows` is inspired by **lf** (List File Manager).
- **lf**: [https://github.com/gokcehan/lf](https://github.com/gokcehan/lf)

Thanks to the `lf` community for the excellent interaction paradigm. `lf-windows` aims to bring this efficient experience to the Windows native GUI environment.
