extern alias SnapshotGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Generator = SnapshotGenerator::Eventuous.Shared.Generators.SnapshotMappingsGenerator;

namespace Eventuous.Tests.Snapshots;

public class GeneratorTests {
    [Test, Category("Contract")]
    public void Non_generic_attribute_registers_snapshot_and_storage_strategy() {
        var generated = Generate("[Snapshots(typeof(MySnapshot), StorageStrategy = SnapshotStorageStrategy.SeparateStore)]");
        generated.ShouldContain("SnapshotTypeMap.Register(typeof(global::Example.MyState), typeof(global::Example.MySnapshot), SnapshotStorageStrategy.SeparateStore)");
    }

    [Test, Category("KnownDefect")]
    public void Generic_attribute_used_by_banking_sample_must_register_snapshot_type() {
        var generated = Generate("[Snapshots<MySnapshot>(StorageStrategy = SnapshotStorageStrategy.SeparateStore)]");
        generated.ShouldContain("SnapshotTypeMap.Register(typeof(global::Example.MyState), typeof(global::Example.MySnapshot), SnapshotStorageStrategy.SeparateStore)");
    }

    [Test, Category("KnownDefect")]
    public void Generic_attribute_constructor_strategy_must_be_respected() {
        var generated = Generate("[Snapshots<MySnapshot>(SnapshotStorageStrategy.SeparateStream)]");
        generated.ShouldContain("SnapshotTypeMap.Register(typeof(global::Example.MyState), typeof(global::Example.MySnapshot), SnapshotStorageStrategy.SeparateStream)");
    }

    [Test, Category("Contract")]
    public void Multiple_snapshot_contract_versions_are_registered() {
        var generated = Generate("[Snapshots(typeof(MySnapshot), typeof(OlderSnapshot), StorageStrategy = SnapshotStorageStrategy.SeparateStore)]");
        generated.ShouldContain("typeof(global::Example.MySnapshot)");
        generated.ShouldContain("typeof(global::Example.OlderSnapshot)");
    }

    static string Generate(string attribute) {
        var source = $$"""
            using Eventuous;
            namespace Example;
            public record MySnapshot(int Count);
            public record OlderSnapshot(int Count);
            {{attribute}}
            public record MyState : State<MyState> { }
            """;
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(new[] { typeof(State<>).Assembly.Location, typeof(SnapshotsAttribute).Assembly.Location }).Distinct();
        // The generator links its own copy of SnapshotStorageStrategy; do not expose that assembly to the probe.
        var references = paths.Where(path => path != typeof(Generator).Assembly.Location)
            .Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        var compilation = CSharpCompilation.Create("GeneratorProbe", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty("The source fixture must compile before testing the generator.");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty("Generated code must compile.");
        return string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
    }
}
