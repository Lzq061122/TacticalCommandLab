// BannerlordProbe / 脚本化时间线驱动
//
// 目的：Phase 10 的 H1/H2/H3 必须可复现。脚本按"任务时间"触发，
// 同一份 script.txt 在同一场战斗里应当产出同一条时间线（战斗本身是否可复现是另一个问题，另行登记）。
//
// 语法（一行一条，# 开头为注释，t 省略则立即执行）：
//   t=5.0  acquire <entity> <domain> <level> <ttl>
//   t=5.5  cmd     <entity> <domain> <HOLD|ADVANCE|RETREAT>
//   t=15.0 release <entity> <domain>
//   t=1.0  policy  enforceNonGated=true
//   t=20.0 dump    <tag>
// entity 形如 P1 / N3（P=玩家队，N=非玩家队，数字为 Formation.Index）
// domain 取 Movement / Facing / Arrangement

using System.Globalization;
using CommandLab.Core;

namespace CommandLab.BannerlordProbe;

public sealed class ScriptRunner
{
    private readonly ProbeRuntime _runtime;
    private readonly string _path;
    private readonly List<KeyValuePair<double, string>> _items = new List<KeyValuePair<double, string>>();
    private int _cursor;

    public ScriptRunner(ProbeRuntime runtime, string path)
    {
        _runtime = runtime;
        _path = path;
        Reload();
    }

    public int Count => _items.Count;
    public string? LoadError { get; private set; }

    public void Reload()
    {
        _items.Clear();
        _cursor = 0;
        try
        {
            if (!System.IO.File.Exists(_path))
            {
                LoadError = "script not found: " + _path;
                return;
            }
            foreach (var rawLine in System.IO.File.ReadAllLines(_path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                double at = 0.0;
                if (line.StartsWith("t=", StringComparison.OrdinalIgnoreCase))
                {
                    var sp = line.IndexOf(' ');
                    if (sp < 0) continue;
                    var num = line.Substring(2, sp - 2);
                    if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out at)) continue;
                    line = line.Substring(sp + 1).Trim();
                }
                _items.Add(new KeyValuePair<double, string>(at, line));
            }
            _items.Sort((a, b) => a.Key.CompareTo(b.Key));
            LoadError = null;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
    }

    public void Tick(double now)
    {
        while (_cursor < _items.Count && _items[_cursor].Key <= now)
        {
            Execute(_items[_cursor].Value);
            _cursor++;
        }
    }

    private void Execute(string line)
    {
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        switch (parts[0].ToLowerInvariant())
        {
            case "acquire": TryAcquire(parts); break;
            case "cmd": TryCommand(parts); break;
            case "release": TryRelease(parts); break;
            case "renew": TryRenew(parts); break;
            case "policy": ApplyPolicy(parts); break;
            case "dump": _runtime.Dump(parts.Length > 1 ? parts[1] : "script"); break;
            default: break;
        }
    }

    /// <summary>
    /// 按实体名解析绑定。解析失败必须留痕：
    /// 静默 return 会让"实体名写错 / 编队不存在 / 绑定未建立"与"指令被执行了但没有效果"
    /// 在产物上完全同形（2026-09-22 实测踩过：census 全空却查不到原因）。
    /// </summary>
    private FormationBinding? Resolve(string entityId)
    {
        var binding = _runtime.FindBinding(entityId);
        if (binding is null) _runtime.Note("SCRIPT_ENTITY_UNRESOLVED " + entityId);
        return binding;
    }

    private static bool TryDomain(string text, out CommandDomain domain) =>
        Enum.TryParse(text, true, out domain);

    private void TryAcquire(string[] parts)
    {
        if (parts.Length < 3) return;
        if (!TryDomain(parts[2], out var domain)) return;
        var binding = Resolve(parts[1]);
        if (binding is null) return;

        var level = _runtime.Config.Level;
        if (parts.Length > 3) Enum.TryParse(parts[3], true, out level);

        var ttl = _runtime.Config.TtlSeconds;
        if (parts.Length > 4) double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out ttl);

        var savedLevel = _runtime.Config.Level;
        var savedTtl = _runtime.Config.TtlSeconds;
        _runtime.Config.Level = level;
        _runtime.Config.TtlSeconds = ttl;
        _runtime.Acquire(binding.Formation, domain, _runtime.Clock.Now);
        _runtime.Config.Level = savedLevel;
        _runtime.Config.TtlSeconds = savedTtl;
    }

    private void TryCommand(string[] parts)
    {
        if (parts.Length < 4) return;
        if (!TryDomain(parts[2], out var domain)) return;
        if (!NeutralOrders.TryParse(parts[3], out var value)) return;
        var binding = Resolve(parts[1]);
        if (binding is null) return;
        _runtime.Command(binding.Formation, domain, value);
    }

    private void TryRelease(string[] parts)
    {
        if (parts.Length < 3) return;
        if (!TryDomain(parts[2], out var domain)) return;
        var binding = Resolve(parts[1]);
        if (binding is null) return;
        _runtime.Release(binding.Formation, domain, _runtime.Clock.Now);
    }

    private void TryRenew(string[] parts)
    {
        if (parts.Length < 3) return;
        if (!TryDomain(parts[2], out var domain)) return;
        var binding = Resolve(parts[1]);
        if (binding is null) return;
        _runtime.Renew(binding.Formation, domain, _runtime.Clock.Now);
    }

    private void ApplyPolicy(string[] parts)
    {
        var cfg = _runtime.Config;
        for (int i = 1; i < parts.Length; i++)
        {
            var kv = parts[i].Split('=');
            if (kv.Length != 2) continue;
            var key = kv[0].ToLowerInvariant();
            var val = kv[1];
            switch (key)
            {
                case "enforcegated": cfg.EnforceOnGated = ParseBool(val, cfg.EnforceOnGated); break;
                case "enforcenongated": cfg.EnforceOnNonGated = ParseBool(val, cfg.EnforceOnNonGated); break;
                case "patchmovement": cfg.PatchMovement = ParseBool(val, cfg.PatchMovement); break;
                case "patchfacing": cfg.PatchFacing = ParseBool(val, cfg.PatchFacing); break;
                case "patcharrangement": cfg.PatchArrangement = ParseBool(val, cfg.PatchArrangement); break;
                case "arrangementpath": Enum.TryParse(val, true, out WritePath p); cfg.ArrangementPath = p; break;
                case "level": Enum.TryParse(val, true, out AuthorityLevel lv); cfg.Level = lv; break;
                case "ttl": double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ttl); cfg.TtlSeconds = ttl; break;
                case "autorenew": cfg.AutoRenew = ParseBool(val, cfg.AutoRenew); break;
                case "controlledbyairoute": cfg.UseControlledByAiRoute = ParseBool(val, cfg.UseControlledByAiRoute); break;
                case "restore": Enum.TryParse(val, true, out ReleaseRestoreMode rm); cfg.Restore = rm; break;
                case "restorebeforerelease": cfg.RestoreBeforeRelease = ParseBool(val, cfg.RestoreBeforeRelease); break;
                default: break;
            }
        }
        // 策略对象不可变，用新对象整体替换（判定逻辑仍在 Core 的写入口内）。
        _runtime.Port.SetPolicy(cfg.ToPolicy());
    }

    private static bool ParseBool(string text, bool fallback) =>
        bool.TryParse(text, out var b) ? b : fallback;
}
