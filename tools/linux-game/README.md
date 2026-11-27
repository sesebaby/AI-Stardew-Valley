# 在 Linux 开发机上跑真实游戏（开发专用）

不改游戏目录，也不改玩家的 Mods。每个实例都放在 `/dev/shm/aisv/<实例名>/`：游戏、Mods、存档（`xdg/`）、日志（`console.log`）和测试工具的命令队列（`io/`）。

```bash
tools/linux-game/setup.sh a            # 准备实例 a（min：只放联动要测的几个 mod；full：玩家全套）
python3 -I tools/linux-game/fakeai.py & # 本机假 AI，端口 18080
tools/linux-game/run.sh a              # 后台启动
python3 -I tools/linux-game/h.py a wait title
python3 -I tools/linux-game/h.py a load AISVTestSp2_455031485
python3 -I tools/linux-game/h.py a --flow tools/linux-game/flows/smoke.txt
tools/linux-game/deploy.sh a           # 改完代码重新构建、放进实例（要先 stop 再 run）
tools/linux-game/stop.sh a             # 只停这个实例；别的会话也在跑游戏，不要按进程名杀
```

## 需要的本机文件

- `~/aisv-game`：游戏的本地副本（Windows 版 1.6.15，带 Linux 的 SDL2、openal）。
- `~/aisv-smapi/SMAPI-4.5.2-installer.zip` 和 `libSkiaSharp.so`：和玩家同版本的 SMAPI，以及它读 png 要用的 Linux 原生库。
- `~/aisv-base-mods`：玩家的全套 mod。
- `~/.nuget/packages` 里的 HarmonyX 2.16.1 和 MonoMod：.NET 8 上 SMAPI 自带的 Harmony 会段错误，`setup.sh` 换成 HarmonyX，并用 `patcher` 跳过 SMAPI 对旧 Harmony 的临时补丁。
- 测试存档：`~/.config/StardewValley/Saves/AISVTest*`，`setup.sh` 复制进实例，测试里的改动不会写回。

## 测试工具（harness/）

一个只在这里用的 SMAPI mod：`h.py` 往 `io/in/` 放命令，它在游戏主线程执行，把结果写到 `io/out/`。

| 命令 | 作用 |
|---|---|
| `status` | 当前状态（地点、时间、菜单、在线的农夫……） |
| `load <存档> [host]` | 读档；带 `host` 就以联机房主身份打开 |
| `join [地址] [名字]` | 以农场帮手加入（默认 127.0.0.1），新角色自动建好 |
| `wait <条件> [参数]` | world、free、title、menu &lt;类型名&gt;、nomenu、dialogue、farmers &lt;人数&gt;、time &lt;时间&gt;、day &lt;总天数&gt;、ticks &lt;帧数&gt; |
| `console <命令...>` | 执行 SMAPI 控制台命令（经过标准输入的 FIFO；输出看 console.log） |
| `screenshot <名字>` | 截图到 `io/out/<名字>.png` |
| `warp <地点> x y`、`time <时间>`、`money <钱>`、`friend <村民> <好感点数>`、`marry <村民>` | 直接改（250 点一颗心；marry 只改婚姻记录、把房子升到 1 级，不搬家） |
| `hold <物品id> [数量]` | 放进背包第一格拿在手上（送礼、交任务） |
| `dump-texture <资源名> <文件>` | 把贴图存成 png（找原版素材的位置） |
| `sleep` | 上床睡到第二天（联机时每个实例都要发） |
| `talk <村民>`、`near <村民>`、`give <村民>` | 走到村民旁边点他 / 只站过去 / 把手上的东西给他 |
| `click x y`、`text <字>`、`key <键>`、`close` | 操作当前菜单 |
| `get <类型.成员...>`、`invoke <类型> <静态方法> [参数...]` | 读静态成员、调静态方法 |
| `dump-asset <资源名> [类型全名]` | 导出资源（默认按字符串字典读，比如日程；别的类型写全名，比如 StardewValley.GameData.Weddings.WeddingData） |
| `build-cabin x y`、`wallets separate\|shared` | 联机测试用 |
| `save`、`title` | 存档、回标题 |

## 联机

两个实例在同一台机器上：一个 `load <存档> host`，另一个 `join 127.0.0.1`。

- 测试存档 `AISVTestMP_455031485` 里已经有一间小屋和农场帮手 Bob。
- 游戏的 Windows 版只带了 `liblwjgl_lz4.dll`，Linux 上联机发消息会崩，所以测试工具把网络压缩换成了游戏自带的不压缩实现。两边都装着测试工具，所以一致；这一点和玩家环境不同。
- 房主占用 UDP 24642，开之前确认没被别的会话占用：`ss -uln | grep 24642`。

## 和玩家环境的差别

| 项 | 这里 | 玩家 |
|---|---|---|
| 系统 | Linux | Windows |
| .NET | 8 | 6 |
| Harmony | HarmonyX 2.16.1 | SMAPI 自带 |
| Steam | 没有，游戏报错后照常运行 | 有 |
| 联机压缩 | 不压缩 | LZ4 |

所以这里能验证游戏逻辑、界面和数据，不能代替在 Windows 上试一次。
