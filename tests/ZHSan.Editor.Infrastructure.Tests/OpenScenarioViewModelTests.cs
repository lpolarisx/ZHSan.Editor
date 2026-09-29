using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class OpenScenarioViewModelTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("zhsan-open-panel-").FullName;

    [Fact]
    public void FirstOpen_RequiresBothFilesBeforeLoading()
    {
        var model = new OpenScenarioViewModel(null, null, _ => null);
        Assert.False(model.CanLoad);
        Assert.Null(model.GetSelection());

        model.ScenarioPath = Create("Scenario.dat");
        Assert.False(model.CanLoad);
        Assert.Contains("CommonData", model.ValidationMessage);

        model.CommonPath = Create("CommonData.dat");
        Assert.True(model.CanLoad);
        var selection = model.GetSelection();
        Assert.Equal(model.ScenarioPath, selection!.ScenarioPath);
        Assert.Equal(model.CommonPath, selection.CommonPath);
    }

    [Fact]
    public void CurrentCommon_TakesPriorityOverAssociationAndCanBeChanged()
    {
        var current = Create("Current.dat");
        var associated = Create("Associated.dat");
        var model = new OpenScenarioViewModel(Create("Scenario.dat"), current, _ => associated);

        Assert.Equal(current, model.CommonPath);
        Assert.Contains("沿用", model.CommonHint);
        model.CommonPath = associated;
        Assert.Equal(associated, model.GetSelection()!.CommonPath);
    }

    [Fact]
    public void ChangingScenario_RefreshesAssociationUntilCommonIsChosenExplicitly()
    {
        var first = Create("First.dat");
        var second = Create("Second.dat");
        var firstCommon = Create("FirstCommon.dat");
        var secondCommon = Create("SecondCommon.dat");
        var model = new OpenScenarioViewModel(first, null, path => path == first ? firstCommon : secondCommon);
        Assert.Equal(firstCommon, model.CommonPath);

        model.ScenarioPath = second;
        Assert.Equal(secondCommon, model.CommonPath);
        model.CommonPath = firstCommon;
        model.ScenarioPath = first;
        model.ScenarioPath = second;
        Assert.Equal(firstCommon, model.CommonPath);
    }

    [Fact]
    public void MissingAssociation_IsShownAndMustBeReplaced()
    {
        var missing = Path.Combine(_directory, "Missing.dat");
        var model = new OpenScenarioViewModel(Create("Scenario.dat"), null, _ => missing);
        Assert.Equal(missing, model.CommonPath);
        Assert.False(model.CanLoad);
        Assert.Contains("更换", model.ValidationMessage);
        model.CommonPath = Create("Common.dat");
        Assert.True(model.CanLoad);
    }

    [Fact]
    public void SameFileOrInvalidPath_CannotLoad()
    {
        var scenario = Create("Scenario.dat");
        var model = new OpenScenarioViewModel(scenario, null, _ => scenario);
        Assert.False(model.CanLoad);
        Assert.Contains("不同", model.ValidationMessage);
        model.ScenarioPath = "\0";
        Assert.False(model.CanLoad);
        Assert.Null(model.GetSelection());
    }

    private string Create(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
