using System.Text;
using PbiBench.Core.Platform;

if (args.Length != 1) throw new ArgumentException("Usage: FeatureCatalogGenerator <repository-root>");
var directory = Path.Combine(Path.GetFullPath(args[0]), "docs", "architecture");
var provenance = ProvenanceCatalog.Parse(File.ReadAllText(Path.Combine(directory, "provenance.json")));
var catalog = FeatureCatalog.Parse(File.ReadAllText(Path.Combine(directory, "feature_catalog.json")), provenance);
var target = Path.Combine(directory, "FEATURE_CATALOG.md");
File.WriteAllText(target, catalog.ToMarkdown(provenance), new UTF8Encoding(false));
Console.WriteLine(target);
