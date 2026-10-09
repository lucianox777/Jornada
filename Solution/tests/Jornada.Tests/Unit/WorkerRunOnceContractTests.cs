namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class WorkerRunOnceContractTests
{
    private static string Root()
    {
        var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"Solution")))d=d.Parent;
        return d?.FullName??throw new DirectoryNotFoundException();
    }

    [Test]
    public void Continuous_workers_keep_resident_default_and_offer_explicit_finite_mode()
    {
        var root=Root();
        var processor=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.Processor.Worker","Program.cs"));
        var processorSettings=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.Processor.Worker","appsettings.json"));
        var operations=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.Operations.Maintenance.Worker","Program.cs"));
        var operationsSettings=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.Operations.Maintenance.Worker","appsettings.json"));
        var bronze=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.Bronze.Maintenance.Worker","Program.cs"));
        var bronzeSettings=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.Bronze.Maintenance.Worker","appsettings.json"));

        Assert.Multiple((Action)(()=>{
            Assert.That(processor,Does.Contain("PROCESS_UNTIL_IDLE"));
            Assert.That(processor,Does.Contain("Processor:RunOnce"));
            Assert.That(processor,Does.Contain("legacyFiniteMode"));
            Assert.That(processor,Does.Contain("finiteTimeout.Token"));
            Assert.That(processorSettings,Does.Contain("\"RunOnce\": false"));
            Assert.That(processor,Does.Contain("Processor:RunOnceMaxSeconds"));
            Assert.That(processor,Does.Contain("Environment.ExitCode = JornadaExitCodes.INCOMPLETE"));
            Assert.That(processorSettings,Does.Contain("\"RunOnceMaxSeconds\": 300"));

            Assert.That(operations,Does.Contain("MaintenanceExecution:RunOnce"));
            Assert.That(operations,Does.Contain("MaintenanceExecution:RunOnceMaxSeconds"));
            Assert.That(operations,Does.Contain("finiteTimeout.Token"));
            Assert.That(operations,Does.Contain("JornadaExitCodes.INCOMPLETE"));
            Assert.That(operations,Does.Contain("RunOnceStepAsync"));
            Assert.That(operations,Does.Contain("ITEM_PROCESSED_RETENTION"));
            Assert.That(operations,Does.Contain("DELIVERY_BRONZE_RETENTION"));
            Assert.That(operations,Does.Contain("PIPELINE_WATCHDOG"));
            Assert.That(operations,Does.Contain("Environment.ExitCode = JornadaExitCodes.FAILURE"));
            Assert.That(operationsSettings,Does.Contain("\"RunOnce\": false"));
            Assert.That(operationsSettings,Does.Contain("\"RunOnceMaxSeconds\": 300"));

            Assert.That(bronze,Does.Contain("BronzeMaintenance:RunOnce"));
            Assert.That(bronze,Does.Contain("BronzeMaintenance:RunOnceMaxSeconds"));
            Assert.That(bronze,Does.Contain("RunCycleAsync(finiteTimeout.Token)"));
            Assert.That(bronze,Does.Contain("JornadaExitCodes.INCOMPLETE"));
            Assert.That(bronzeSettings,Does.Contain("\"RunOnce\": false"));
            Assert.That(bronzeSettings,Does.Contain("\"RunOnceMaxSeconds\": 300"));
        }));
    }
}
