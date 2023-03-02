using System.CommandLine;
using System.CommandLine.Binding;
using Csv;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using netDxf;

namespace dxfcriber;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("dxfcriber extracts TEXT entities from DXF files and outputs their data to a CSV file.");

        var inputFileArgument = new Argument<string[]?>(
            name: "input_file",
            description: "input DXF files",
            isDefault: true,
            parse: result =>
            {
                var dir = new DirectoryInfoWrapper(new DirectoryInfo("."));
                var globbed = new List<string>();
                foreach (var token in result.Tokens)
                {
                    globbed.AddRange(new Matcher().AddInclude(token.Value).Execute(dir).Files.Select(f => f.Path));
                }
                if (globbed.Count == 0)
                {
                    result.ErrorMessage = "No input file";
                    return null;
                }
                return globbed.ToArray();
            });

        var columnsOption = new Option<Column[]>(
            aliases: new[] { "--column", "-c" },
            description: "Column name, min position X to group, and max position X to group separated by comma (,)",
            parseArgument: result =>
            {
                var columns = new List<Column>();
                foreach (var token in result.Tokens)
                {
                    var parts = token.Value.Split(',');
                    switch (parts.Length)
                    {
                        case 2:
                            columns.Add(new Column(parts[0], double.Parse(parts[1]), double.Parse(parts[1])));
                            break;
                        case 3:
                            columns.Add(new Column(parts[0], double.Parse(parts[1]), double.Parse(parts[2])));
                            break;
                    }
                }
                return columns.ToArray();
            }
        );

        var roundOption = new Option<double?>(
            aliases: new[] { "--round", "-r" },
            description: "Multiple to round positional numbers"
        );

        var xMinOption = new Option<double?>(
            name: "--xmin",
            description: "Minimum position X to extract TEXT entities"
        );

        var xMaxOption = new Option<double?>(
            name: "--xmax",
            description: "Maximum position X to extract TEXT entities"
        );

        var yMinOption = new Option<double?>(
            name: "--ymin",
            description: "Minimum position Y to extract TEXT entities"
        );

        var yMaxOption = new Option<double?>(
            name: "--ymax",
            description: "Maximum position Y to extract TEXT entities"
        );

        var colorOption = new Option<short?>(
            name: "--color",
            description: "Index color number to extract TEXT entities"
        );

        var layerOption = new Option<string?>(
            name: "--layer",
            description: "Layer name to extract TEXT entities"
        );

        rootCommand.AddArgument(inputFileArgument);
        rootCommand.AddOption(columnsOption);
        rootCommand.AddOption(roundOption);
        rootCommand.AddOption(xMinOption);
        rootCommand.AddOption(xMaxOption);
        rootCommand.AddOption(yMinOption);
        rootCommand.AddOption(yMaxOption);
        rootCommand.AddOption(colorOption);
        rootCommand.AddOption(layerOption);

        rootCommand.SetHandler((files, columns, round, filter) =>
        {
            Console.WriteLine(ExtractText(files!, columns, round, filter));
        }, inputFileArgument, columnsOption, roundOption, new FilterBinder(xMinOption, xMaxOption, yMinOption, yMaxOption, colorOption, layerOption));

        return await rootCommand.InvokeAsync(args);
    }

    internal static string ExtractText(string[] files, Column[] cols, double? round, Filter filter)
    {
        var rawData = new Dictionary<(string filename, double y), Dictionary<double, string>>();
        var xPositions = new List<Column>();

        // Read each DXF file
        foreach (var file in files)
        {
            var dxf = DxfDocument.Load(file);
            foreach (var text in dxf.Entities.Texts)
            {
                var xpos = text.Position.X;
                var ypos = text.Position.Y;

                // Round positional numbers
                if (round != null)
                {
                    xpos = Math.Truncate(xpos / (double)round) * (double)round;
                    ypos = Math.Truncate(ypos / (double)round) * (double)round;
                }

                // Filter data
                if (filter.Xmin != null && xpos < filter.Xmin) continue;
                if (filter.Xmax != null && xpos > filter.Xmax) continue;
                if (filter.Ymin != null && ypos < filter.Ymin) continue;
                if (filter.Ymax != null && ypos > filter.Ymax) continue;
                if (filter.Color != null && text.Color.Index != filter.Color) continue;
                if (filter.Layer != null && text.Layer.Name != filter.Layer) continue;

                // Store data
                xPositions.Add(new Column("x=" + xpos, xpos, xpos));
                var key = (filename: file, y: ypos);
                if (!rawData.ContainsKey(key))
                {
                    rawData.Add(key, new Dictionary<double, string>());
                }
                rawData[key][xpos] = text.Value;
            }
        }

        // Make merged columns
        var columns = cols.ToList();
        if (columns.Count == 0)
        {
            columns.AddRange(xPositions.DistinctBy(c => c.Xmin).OrderBy(c => c.Xmin));
        }

        // Make row data
        var rows = new List<string[]>();
        foreach (var data in rawData.OrderBy(r => r.Key.filename).ThenByDescending(r => r.Key.y))
        {
            var row = new List<string>();
            foreach (var col in columns)
            {
                row.Add(data.Value.FirstOrDefault(x => x.Key >= col.Xmin && x.Key <= col.Xmax, new KeyValuePair<double, string>(0, "")).Value);
            }
            if (row.All(r => r == "")) continue;
            row.InsertRange(0, new[] { data.Key.filename, data.Key.y.ToString() });
            rows.Add(row.ToArray());
        }

        var outc = new string[] { "filename", "y" };
        return CsvWriter.WriteToText(outc.Concat(columns.Select(c => c.Name)).ToArray(), rows, ',');
    }

    public record Column(string Name, double Xmin, double Xmax);

    public class Filter
    {
        public double? Xmin { get; set; }
        public double? Xmax { get; set; }
        public double? Ymin { get; set; }
        public double? Ymax { get; set; }
        public short? Color { get; set; }
        public string? Layer { get; set; }
    }

    public class FilterBinder : BinderBase<Filter>
    {
        private readonly Option<double?> _xmin;
        private readonly Option<double?> _xmax;
        private readonly Option<double?> _ymin;
        private readonly Option<double?> _ymax;
        private readonly Option<short?> _color;
        private readonly Option<string?> _layer;

        public FilterBinder(Option<double?> xmin, Option<double?> xmax, Option<double?> ymin, Option<double?> ymax, Option<short?> color, Option<string?> layer)
        {
            _xmin = xmin;
            _xmax = xmax;
            _ymin = ymin;
            _ymax = ymax;
            _color = color;
            _layer = layer;
        }

        protected override Filter GetBoundValue(BindingContext bindingContext) =>
            new Filter
            {
                Xmin = bindingContext.ParseResult.GetValueForOption(_xmin),
                Xmax = bindingContext.ParseResult.GetValueForOption(_xmax),
                Ymin = bindingContext.ParseResult.GetValueForOption(_ymin),
                Ymax = bindingContext.ParseResult.GetValueForOption(_ymax),
                Color = bindingContext.ParseResult.GetValueForOption(_color),
                Layer = bindingContext.ParseResult.GetValueForOption(_layer)
            };
    }
}
