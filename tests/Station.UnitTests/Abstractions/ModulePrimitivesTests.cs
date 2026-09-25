using FluentAssertions;
using Station.Abstractions.Enums;
using Station.Abstractions.Models;

namespace Station.UnitTests.Abstractions;

public sealed class ModulePrimitivesTests
{
    [Fact]
    public void ModuleResult_helpers_populate_flags()
    {
        ModuleResult
            .NoChange("same")
            .Should()
            .BeEquivalentTo(new ModuleResult(false, false, false, "same", null));
        ModuleResult.ChangedResult("changed").Changed.Should().BeTrue();
        ModuleResult.SkippedResult("skipped").Skipped.Should().BeTrue();
        ModuleResult.Failure("failed").Failed.Should().BeTrue();
        ModuleResult.NoChange().OutputsOrEmpty.Should().NotBeNull();
    }

    [Fact]
    public void ModuleInspectionResult_helpers_track_validity()
    {
        ModuleInspectionResult
            .Success(PrivilegeRequirement.Administrator)
            .IsValid.Should()
            .BeTrue();
        ModuleInspectionResult.Failure(new ModuleDiagnostic("broken")).IsValid.Should().BeFalse();
    }
}
