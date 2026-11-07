# 数据说明 · Data Notice

[中文](#中文) · [English](#english)

## 中文

只有使用发布版内置的 AI星露谷服务器时，服务器才会收到下面这些信息。改用自己的 AI 网关后，这些信息都不会发给 AI星露谷服务器。游戏里也能看这份说明：设置 > 常用 > 数据说明。

### 服务必需的（一直会收）

- **账号**：用户名。密码只存加密后的哈希，看不到原文。
- **登录**：登录时的 IP 和电脑名。用来显示你在哪些电脑上登录过，也用来防盗号。
- **AI 用量**：每次 AI 请求的时间、用了多少 token、花了多少额度。用来扣额度和限速。
- **机器码**：系统自带的机器 ID 加盐后算出的 SHA-256 哈希，只取前 32 位，推不出原值。只用来防刷号、拉黑恶意账号。

### 用于改进功能的（可以关）

- **游戏时长**：存档开着、游戏窗口在前台的时间，每隔几分钟报一次。
- **功能次数**：打开了哪些页面、应用，用了几次手机和电脑。只报次数，键名是代码里写死的名字。
- **AI 请求属于哪个功能**：比如和村民聊天、居民群、来信。

这些数据只用来了解大家喜欢哪些功能、改进这个 mod：不卖、不给别人、不做广告。默认开着，在 设置 > 常用 > 帮助改进功能 里可以关；关掉后就不再收集，还没发出去的也会丢掉。

### 不会收集的

- 你和村民聊天的内容、存档内容、你写的任何文字。服务器只把请求转给 AI，不保存。
- 系统版本、硬件配置、MAC 地址、真实姓名、联系方式。

使用记录默认保留 90 天，之后自动删除。想删除账号，联系作者（见 README 的“联系与反馈”）。

## English

The AI Stardew Valley server only receives the information below when you use the server built into the release package. If you switch to your own AI gateway, none of it is sent to the AI Stardew Valley server. You can also read this notice in game: Settings > Common > Data notice.

### Needed to run the service (always collected)

- **Account**: your user name. Passwords are stored only as hashes; nobody can read the original.
- **Sign-in**: the IP address and computer name used to sign in. They show which computers you are signed in on and help stop account theft.
- **AI usage**: the time, token count and cost of each AI request. They are used to charge your allowance and to apply rate limits.
- **Machine ID**: a salted SHA-256 hash of the system machine ID, cut to 32 characters. The original ID cannot be recovered from it. It is used only to stop mass sign-ups and to block abusive accounts.

### Used to improve the mod (can be turned off)

- **Play time**: how long a save is open with the game window in front. It is reported every few minutes.
- **Feature counts**: which pages and apps you open, and how often you use the phone and the computer. Only counts are sent, and the keys are fixed names from the code.
- **Which feature each AI request belongs to**: for example, villager chat, the residents' group or letters.

This data is used only to learn which features players like and to improve the mod. It is never sold, shared or used for ads. It is on by default. You can turn it off at Settings > Common > Help improve the mod. Once it is off, nothing more is collected, and anything not yet sent is discarded.

### Never collected

- What you say to villagers, your save data, or anything else you type. The server only forwards requests to the AI and does not store them.
- Your OS version, hardware details, MAC address, real name or contact details.

Usage records are kept for 90 days by default and then deleted automatically. To delete your account, contact the author (see "联系与反馈" in the README).
