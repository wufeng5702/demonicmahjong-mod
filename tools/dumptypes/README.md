# dumptypes — IL2CPP interop 类型探查工具

MonCecil 小程序，直接读取 BepInEx 生成的 interop 程序集（`BepInEx\interop\*.dll`）里的
**公有成员清单**（字段/属性/公开方法），用于在写 BepInEx 插件前确认某个类型在
新版 Il2CppInterop 门面里到底暴露了什么。

## 用法

```powershell
dotnet build -c Release              # 首次构建
dotnet run --no-build -c Release -- "<interop.dll>" "<类型全名或 *前缀>"
dotnet run --no-build -c Release -- "<interop.dll>" "--xref" "<目标类型全名>" "[方法名]"
```

- 第二个参数是**精确全名**，或以 `*` 开头做**包含匹配**（只剥前导 `*`，**勿带尾 `*`**）。
  例：`"MaJiang.PlayMaJiang.RoundStatistics.PlayerRoundStatistics"`、`"*RoundStatistics"`。
- `--xref` 模式：扫描**同一 interop 程序集内**谁调用了目标（仅供确认给定类型/方法
  是否被 managed wrapper 引用；游戏原生代码在 GameAssembly.dll 里，查不到真调用方）。
- 泛型全名含反引号，bash 里要用单引号包裹，如 `'Il2CppSystem.Collections.Generic.HashSet`1'`。

## 已知经验（配合查询）

- interop 中 `IReadOnlyDictionary`/`IReadOnlyList`/`IEnumerable` **接口门面成员为空**；
  实际数值读具体类（`Dictionary<K,V>` 的 `_entries`/`Count`、`List<T>` 的 `Item`/`Count`）。
- `HashSet<T>` 具体类有 `_slots`(`Slot{T}.value/next/hashCode`)/`_buckets`/`_count`。
- 查询不到 = 类型/成员不在该程序集内，或模式写错（`*` 陷阱）。