using CommandLab.Core;
using CommandLab.Telemetry;
using System.Security.Cryptography;
using System.Text.Json;

Console.WriteLine("PHASE11_CODEX_REVIEW_ARBITRATION_ORDER_20261007");
var assembly = typeof(WritePort).Assembly;
Console.WriteLine(JsonSerializer.Serialize(new { assembly.Location, mvid = assembly.ManifestModule.ModuleVersionId, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) }));
object Run(bool competitor)
{
    var clock = new VirtualClock(0.1);
    var telemetry = new TelemetryCollector();
    var authority = new AuthorityService(telemetry);
    var lease = authority.Acquire("E0", CommandDomain.Movement, "Lab", AuthorityLevel.Full, 10, clock.Now)!;
    var port = new WritePort(clock, authority, telemetry, new WritePortPolicy { Arbitration = ArbitrationMode.Priority });
    var entity = new ControlledEntity("E0");
    port.RegisterEntity(entity);
    port.Submit(new WriteRequest { EntityId = "E0", Domain = CommandDomain.Movement, Value = CommandValue.HOLD, WriterId = "Lab", LeaseId = lease.LeaseId, LeaseToken = lease.Token, Generation = lease.Generation, Priority = 1 });
    if (competitor)
        port.Submit(new WriteRequest { EntityId = "E0", Domain = CommandDomain.Movement, Value = CommandValue.ADVANCE, WriterId = "Native", Priority = 100 });
    port.FlushTick();
    var attempts = telemetry.Attempts.Select(a => new { a.WriterId, a.Rejected, a.RejectReasonText, a.OrderChanged }).ToArray();
    return new { competitor, value = entity[CommandDomain.Movement].Value.ToString(), attempts };
}
Console.WriteLine(JsonSerializer.Serialize(new { owner_only = Run(false), unauthorized_high_priority = Run(true) }, new JsonSerializerOptions { WriteIndented = true }));
